using Flurl.Http.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR.Sherpa;
using XiaoZhi.Net.Server.Providers.AudioCodec;
using XiaoZhi.Net.Server.Providers.AudioMixer;
using XiaoZhi.Net.Server.Providers.AudioPlayer;
using XiaoZhi.Net.Server.Providers.AudioPlayer.Music;
using XiaoZhi.Net.Server.Providers.AudioPlayer.SystemNotification;
using XiaoZhi.Net.Server.Providers.IoT;
using XiaoZhi.Net.Server.Providers.LLM;
using XiaoZhi.Net.Server.Providers.LLM.Agents;
using XiaoZhi.Net.Server.Providers.LLM.FunctionInvocationFilters;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Providers.MCP.DeviceMcp;
using XiaoZhi.Net.Server.Providers.MCP.McpEndpoint;
using XiaoZhi.Net.Server.Providers.MCP.ServerMcp;
using XiaoZhi.Net.Server.Providers.Memory;
using XiaoZhi.Net.Server.Providers.TTS;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Native;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;
using XiaoZhi.Net.Server.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;
using XiaoZhi.Net.Server.Services;

namespace XiaoZhi.Net.Server.Management
{
    /// <summary>
    /// 提供程序管理器类 - 负责注册、构建和初始化各种服务组件，包括VAD、ASR、LLM、Memory、TTS等，并处理私有配置的加载和内存的保存
    /// </summary>
    internal class ProviderManager
    {
        /// <summary>
        /// 提供程序管理器，负责注册、构建和初始化各种服务组件
        /// </summary>
        private readonly IServiceProvider _serviceProvider;
        /// <summary>
        /// 全局内核实例
        /// </summary>
        private readonly Kernel _globalKernel;
        /// <summary>
        /// 小智配置对象
        /// </summary>
        private readonly XiaoZhiConfig _config;
        /// <summary>
        /// 日志记录器
        /// </summary>
        private readonly ILogger<ProviderManager> _logger;


        /// <summary>
        /// 初始化 ProviderManager 类的新实例
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        /// <param name="_globalKernel">全局内核</param>
        /// <param name="config">小智配置</param>
        /// <param name="logger">日志记录器</param>
        public ProviderManager(IServiceProvider serviceProvider, Kernel _globalKernel, XiaoZhiConfig config, ILogger<ProviderManager> logger)
        {
            this._serviceProvider = serviceProvider;
            this._globalKernel = _globalKernel;
            this._config = config;
            this._logger = logger;
        }

        /// <summary>
        /// 注册所有必要的服务到主机构建器中
        /// </summary>
        /// <param name="builder">主机构建器</param>
        /// <param name="config">小智配置</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
                RegisterAudioDecoder(services);
                RegisterVad(services, config, GlobalProviderNames.GLOBAL_VAD);
                RegisterAsr(services, config, GlobalProviderNames.GLOBAL_ASR);
                RegisterLlm(services, config);
                RegisterMemory(services, config, GlobalProviderNames.GLOBAL_MEMORY);
                RegisterTts(services, config, GlobalProviderNames.GLOBAL_TTS);

                RegisterAudioEncoder(services);
                RegisterAudioResampler(services);
                RegisterAudioMixer(services);
                RegisterIoT(services);
                RegisterMCP(services);
                RegisterLLMPlugins(services);
                RegisterAudioPlayer(services);

                services.AddSingleton<ProviderManager>();
            });
        }

        /// <summary>
        /// 构建核心组件，包括VAD、ASR、Memory、TTS等，并检查FFmpeg是否安装
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        /// <returns>如果所有组件都成功构建则返回true，否则返回false</returns>
        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            try
            {
                #region Vad
                IVad vad = serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
                if (vad.IsSherpaModel && !vad.Build(this.GetSelectedSetting("VAD", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, vad.ModelName);
                    return false;
                }
                #endregion

                #region Asr
                IAsr asr = serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
                if (asr.IsSherpaModel && !asr.Build(this.GetSelectedSetting("ASR", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, asr.ModelName);
                    return false;
                }
                #endregion

                #region Memory
                IMemory memory = serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY);
                if (!memory.Build(this.GetSelectedSetting("MEMORY", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, memory.ModelName);
                    return false;
                }
                #endregion

                #region Tts
                ITts tts = serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
                if (tts.IsSherpaModel && !tts.Build(this.GetSelectedSetting("TTS", this._config)))
                {
                    this._logger.LogError(Lang.ProviderManager_BuildComponent_ProviderBuildFailed, tts.ModelName);
                    return false;
                }
                #endregion

                #region FFmpeg
                if (MediaFactory.CheckFFmpegInstalled(out string ffmpegVersion))
                {
                    this._logger.LogInformation(Lang.ProviderManager_BuildComponent_FFmpegInstalled, ffmpegVersion);
                }
                else
                {
                    this._logger.LogWarning(Lang.ProviderManager_BuildComponent_FFmpegNotFound);
                    return false;
                }
                #endregion

                return true;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, Lang.ProviderManager_BuildComponent_BuildComponentsFailed);
                return false;
            }
        }

        /// <summary>
        /// 获取指定模型类型的已选设置
        /// </summary>
        /// <param name="selectedModelType">选择的模型类型</param>
        /// <param name="config">小智配置</param>
        /// <returns>模型设置对象</returns>
        private ModelSetting GetSelectedSetting(string selectedModelType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedModelType];
            Dictionary<string, string> setting = config.ConfiguredSettings[selectedModelType][selectedModel];

            ModelSetting modelSetting = new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };

            return modelSetting;
        }

        /// <summary>
        /// 获取指定LLM类型的已选设置
        /// </summary>
        /// <param name="selectedLLMType">选择的LLM类型</param>
        /// <param name="config">小智配置</param>
        /// <returns>模型设置对象</returns>
        private ModelSetting GetSelectedLLMSetting(string selectedLLMType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedLLMType];
            Dictionary<string, string> setting = config.ConfiguredSettings["LLM"][selectedModel];

            ModelSetting modelSetting = new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };

            return modelSetting;
        }

        /// <summary>
        /// 异步初始化私有配置，根据设备ID从远程API加载配置或使用全局配置
        /// </summary>
        /// <param name="session">会话对象</param>
        /// <returns>如果配置初始化成功则返回true，否则返回false</returns>
        public async Task<bool> InitializePrivateConfigAsync(Session session)
        {
            try
            {
                ManageApiClient? manageApiClient = this._serviceProvider.GetService<ManageApiClient>();

                if (manageApiClient is null)
                {
                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable, session.DeviceId);

                    session.IsDeviceBinded = true; // Assume device is binded if manage API is not available

                    return this.RegisterGlobalProviders(session);
                }

                PrivateModelsConfig? privateModelsConfig = await manageApiClient.LoadConfigFromApi(session.DeviceId, session.SessionId);

                if (privateModelsConfig is null)
                {
                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_NoPrivateConfig, session.DeviceId);

                    return this.RegisterGlobalProviders(session);
                }

                if (privateModelsConfig.VadSetting is not null)
                {
                    IVad privateVad = this._serviceProvider.GetRequiredKeyedService<IVad>(privateModelsConfig.VadSetting.ModelName);
                    if (!privateVad.Build(privateModelsConfig.VadSetting))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetVad(privateVad);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateVadInitialized, privateModelsConfig.VadSetting.ModelName, session.DeviceId);
                }
                else
                {
                    IVad genericVad = this._serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
                    if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this._config)))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericVadBuildFailed, genericVad.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetVad(genericVad);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericVadInitialized, genericVad.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.AsrSetting is not null)
                {
                    IAsr privateAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(privateModelsConfig.AsrSetting.ModelName);
                    if (!privateAsr.Build(privateModelsConfig.AsrSetting))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(privateAsr);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateAsrInitialized, privateModelsConfig.AsrSetting.ModelName, session.DeviceId);
                }
                else
                {
                    IAsr genericAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
                    if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this._config)))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericAsrBuildFailed, genericAsr.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetAsr(genericAsr);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericAsrInitialized, genericAsr.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.EmotionLlmSetting is not null && privateModelsConfig.ChatLlmSetting is not null)
                {
                    // 初始化私有模型配置并构建LLM实例
                    Kernel privateKernel = this._globalKernel.Clone();
                    ILlm privateLlm = this._serviceProvider.GetRequiredService<ILlm>();

                    // 获取私有模型配置参数
                    string llmModelName = privateModelsConfig.ChatLlmSetting.ModelName;
                    string prompt = privateModelsConfig.ChatLlmSetting.Config.GetConfigValueOrDefault("Prompt", this._config.Prompt);
                    bool useStreaming = privateModelsConfig.ChatLlmSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
                    string summaryMemory = privateModelsConfig.ChatLlmSetting.Config.GetConfigValueOrDefault("SummaryMemory", string.Empty);
                    bool useEmotions = privateModelsConfig.EmotionLlmSetting.Config.GetConfigValueOrDefault("UseEmotions", false);

                    // 创建LLM构建配置对象
                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                        privateModelsConfig.EmotionLlmSetting.ModelName,
                        privateModelsConfig.ChatLlmSetting.ModelName,
                        prompt,
                        useStreaming,
                        useEmotions,
                        summaryMemory,
                        privateKernel);

                    // 将会话数据添加到内核中
                    privateKernel.Data.Add("session", session);
                    if (!privateLlm.Build(llmBuildConfig))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed, session.DeviceId);
                        return false;
                    }
                    // 设置会话的私有提供者内核和LLM实例
                    session.PrivateProvider.SetKernel(privateKernel);
                    session.PrivateProvider.SetLlm(privateLlm);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateLlmInitialized, privateModelsConfig.EmotionLlmSetting.ModelName, privateModelsConfig.ChatLlmSetting.ModelName, session.DeviceId);
                }
                else
                {
                    Kernel privateKernel = this._globalKernel.Clone();
                    ILlm genericLlm = this._serviceProvider.GetRequiredService<ILlm>();

                    ModelSetting emotionLLMModelSetting = this.GetSelectedLLMSetting("EmotionLLM", this._config);
                    ModelSetting chatLLMModelSetting = this.GetSelectedLLMSetting("ChatLLM", this._config);
                    bool useStreaming = chatLLMModelSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
                    bool useEmotions = emotionLLMModelSetting.Config.GetConfigValueOrDefault("UseEmotions", false);

                    LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                        emotionLLMModelSetting.ModelName,
                        chatLLMModelSetting.ModelName,
                        this._config.Prompt,
                        useStreaming,
                        useEmotions,
                        SummaryMemory: string.Empty,
                        privateKernel);

                    privateKernel.Data.Add("session", session);
                    if (!genericLlm.Build(llmBuildConfig))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetKernel(privateKernel);
                    session.PrivateProvider.SetLlm(genericLlm);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericLlmInitialized, emotionLLMModelSetting.ModelName, chatLLMModelSetting.ModelName, session.DeviceId);
                }

                if (privateModelsConfig.TtsSetting is not null)
                {
                    ITts privateTts = this._serviceProvider.GetRequiredKeyedService<ITts>(privateModelsConfig.TtsSetting.ModelName);
                    if (!privateTts.Build(privateModelsConfig.TtsSetting))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed, session.DeviceId);
                        return false;
                    }
                    session.PrivateProvider.SetTts(privateTts);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_PrivateTtsInitialized, privateModelsConfig.TtsSetting.ModelName, session.DeviceId);
                }
                else
                {
                    ITts genericTts = this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
                    if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this._config)))
                    {
                        this._logger.LogError(Lang.ProviderManager_InitializePrivateConfig_GenericTtsBuildFailed, genericTts.ModelName);
                        return false;
                    }
                    session.PrivateProvider.SetTts(genericTts);

                    this._logger.LogInformation(Lang.ProviderManager_InitializePrivateConfig_GenericTtsInitialized, genericTts.ModelName, session.DeviceId);
                }

                return true;
            }
            catch (DeviceNotFoundException)
            {
                session.IsDeviceBinded = false;
                this._logger.LogWarning(Lang.ProviderManager_InitializePrivateConfig_DeviceNotFound, session.DeviceId);
                return true;
            }
            catch (DeviceBindException deviceBindException)
            {
                session.IsDeviceBinded = false;
                session.BindCode = deviceBindException.BindCode;
                this._logger.LogWarning(Lang.ProviderManager_InitializePrivateConfig_DeviceNotBinded, session.DeviceId, session.BindCode);
                return true;
            }
            catch (Exception ex)
            {
                session.IsDeviceBinded = false;
                this._logger.LogError(ex, Lang.ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed, session.DeviceId, session.SessionId);
                return false;
            }
            finally
            {
                this.BuildAudioDecoder(session);
                this.BuildAudioPlayer(session);
                this.BuildAudioProcessor(session);
                this.BuildAudioResampler(session);
                this.BuildAudioEncoder(session);
            }
        }
        /// <summary>
        /// 异步保存会话记忆到远程服务
        /// </summary>
        /// <param name="session">要保存记忆的会话对象</param>
        /// <returns>异步任务</returns>
        public async Task SaveMemoryAsync(Session session)
        {
            // 检查LLM是否已初始化
            if (session.PrivateProvider.Llm is null)
            {
                this._logger.LogWarning(Lang.ProviderManager_SaveMemory_LlmNotInitialized, session.DeviceId);
                return;
            }

            // 检查聊天历史是否存在
            if (session.PrivateProvider.Llm.LLMChatHistory.Any())
            {
                // 获取API客户端服务
                ManageApiClient? manageApiClient = this._serviceProvider.GetService<ManageApiClient>();
                if (manageApiClient is not null)
                {
                    try
                    {
                        // 调用API保存记忆数据
                        await manageApiClient.SaveMemoryAsync(session.DeviceId, session.SessionId, session.PrivateProvider.Llm.LLMChatHistory);
                        this._logger.LogInformation(Lang.ProviderManager_SaveMemory_MemorySaved, session.DeviceId, session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        // 记录保存失败的错误日志
                        this._logger.LogError(ex, Lang.ProviderManager_SaveMemory_SaveMemoryFailed, session.DeviceId, session.SessionId);
                    }
                }
                else
                {
                    // 记录API客户端不可用的警告日志
                    this._logger.LogWarning(Lang.ProviderManager_SaveMemory_ApiClientNotAvailable, session.DeviceId, session.SessionId);
                }
            }
        }

        /// <summary>
        /// 释放指定服务提供者中的全局音频处理服务
        /// </summary>
        /// <param name="serviceProvider">服务提供者实例</param>
        public void Dispose(IServiceProvider serviceProvider)
        {
            // 获取需要释放的全局服务列表
            IList<IDisposable> providers = new List<IDisposable>
            {
                serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR),
                serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD),
                serviceProvider.GetRequiredKeyedService<IMemory>(GlobalProviderNames.GLOBAL_MEMORY)
            };

            // 逐个释放服务资源
            foreach (IDisposable provider in providers)
            {
                provider.Dispose();
            }

        }

        #region Register providers
        #region AudioDecoder
        /// <summary>
        /// 注册音频解码器服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterAudioDecoder(IServiceCollection services)
        {
            services.AddTransient<IAudioDecoder, DefaultOpusDecoder>();
        }

        /// <summary>
        /// 构建并设置会话的音频解码器
        /// </summary>
        /// <param name="session">目标会话对象</param>
        public void BuildAudioDecoder(Session session)
        {
            IAudioDecoder audioDecoder = this._serviceProvider.GetRequiredService<IAudioDecoder>();
            if (!audioDecoder.Build(session.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioEncoder_BuildFailed, session.SessionId);
            }
            session.PrivateProvider.SetAudioDecoder(audioDecoder);
        }
        #endregion

        #region AudioResampler
        /// <summary>
        /// 注册音频重采样器服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterAudioResampler(IServiceCollection services)
        {
            services.AddTransient<IAudioResampler, DefaultResampler>();
        }

        /// <summary>
        /// 构建并设置会话的音频重采样器（当TTS采样率与配置采样率不匹配时）
        /// </summary>
        /// <param name="session">目标会话对象</param>
        public void BuildAudioResampler(Session session)
        {
            // 获取TTS采样率，如果当前TTS为空则使用全局TTS
            int ttsSampleRate = session.PrivateProvider.Tts?.GetTtsSampleRate() ?? this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS).GetTtsSampleRate();

            // 如果采样率相同则无需重采样
            if (ttsSampleRate == this._config.AudioSetting.SampleRate)
            {
                return;
            }

            this._logger.LogInformation(Lang.ProviderManager_BuildAudioResampler_ResamplingRequired, session.DeviceId, ttsSampleRate, this._config.AudioSetting.SampleRate);

            // 创建重采样器构建配置
            ResamplerBuildConfig resamplerBuildConfig = new ResamplerBuildConfig(session.AudioSetting.Channels, ttsSampleRate, this._config.AudioSetting.SampleRate);
            IAudioResampler audioResampler = this._serviceProvider.GetRequiredService<IAudioResampler>();
            if (!audioResampler.Build(resamplerBuildConfig))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioResampler_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetAudioResampler(audioResampler);
            }
        }
        #endregion

        #region AudioEncoder
        /// <summary>
        /// 注册音频编码器服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterAudioEncoder(IServiceCollection services)
        {
            services.AddTransient<IAudioEncoder, DefaultOpusEncoder>();
        }

        /// <summary>
        /// 构建并设置会话的音频编码器
        /// </summary>
        /// <param name="session">目标会话对象</param>
        public void BuildAudioEncoder(Session session)
        {
            IAudioEncoder audioEncoder = this._serviceProvider.GetRequiredService<IAudioEncoder>();
            if (!audioEncoder.Build(this._config.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioEncoder_BuildFailed, session.SessionId);
            }
            session.PrivateProvider.SetAudioEncoder(audioEncoder);
        }
        #endregion

        #region VAD
        /// <summary>
        /// 根据配置注册VAD（语音活动检测）服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="config">小智配置对象</param>
        /// <param name="key">服务键名</param>
        private static void RegisterVad(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["VAD"]);
            switch (modelName)
            {
                case "silero":
                    services.AddKeyedSingleton<IVad, Silero>(key);
                    break;
                case "silero-native":
                    services.AddKeyedTransient<IVad, SileroNative>(modelName);
                    services.AddKeyedTransient<IVad, SileroNative>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid vad model.");
            }
        }
        #endregion

        #region ASR
        /// <summary>
        /// 根据配置注册ASR（自动语音识别）服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="config">小智配置对象</param>
        /// <param name="key">服务键名</param>
        private static void RegisterAsr(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["ASR"]);
            switch (modelName)
            {
                case "sense-voice":
                    services.AddKeyedSingleton<IAsr, SenseVoice>(key);
                    break;
                case "paraformer":
                    services.AddKeyedSingleton<IAsr, Paraformer>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid asr model.");
            }
        }
        #endregion

        #region LLM
        /// <summary>
        /// 根据配置注册LLM（大语言模型）相关服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="config">小智配置对象</param>
        private static void RegisterLlm(IServiceCollection services, XiaoZhiConfig config)
        {
            // 遍历所有LLM配置项并注册相应的OpenAI聊天完成服务
            foreach (var llmSettingItem in config.ConfiguredSettings["LLM"])
            {
                string? endPoint = llmSettingItem.Value.GetConfigValueOrDefault("BaseUrl");
                string? apiKey = llmSettingItem.Value.GetConfigValueOrDefault("ApiKey");
                string? modelId = llmSettingItem.Value.GetConfigValueOrDefault("ModelName");

                if (string.IsNullOrEmpty(endPoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelId))
                {
                    throw new ModelBuildException($"Invalid llm model setting, endPoint: {endPoint}, apiKey: {apiKey}, modelId: {modelId}.");
                }

                switch (llmSettingItem.Key.ToLower())
                {
                    case "qwen":
                    case "doubao":
                    case "deepseek":
                    case "chatglm":
                        services.AddOpenAIChatCompletion(modelId, new Uri(endPoint), apiKey, orgId: "Xiao Zhi", $"LLM_{llmSettingItem.Key}");
                        break;
                    default:
                        throw new ModelBuildException("Invalid llm model.");
                }
            }

            // 注册LLM相关的过滤器、代理等服务
            services.AddTransient<IFunctionInvocationFilter, MCPToolFunctionFilter>();
            services.AddTransient<IEmotionAgent, EmotionAgent>();
            services.AddTransient<IChatAgent, ChatAgent>();
            services.AddTransient<ILlm, GenericOpenAI>();
        }
        #endregion

        #region LLMPlugins
        /// <summary>
        /// 注册LLM插件服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterLLMPlugins(IServiceCollection services)
        {
            services.AddTransient<MusicPlayer>();
        }
        #endregion

        #region Memory
        /// <summary>
        /// 根据配置注册记忆服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="config">小智配置对象</param>
        /// <param name="key">服务键名</param>
        private static void RegisterMemory(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["MEMORY"]);
            switch (modelName)
            {
                case "flash-memory":
                    services.AddKeyedTransient<IMemory, FlashMemory>(modelName);
                    services.AddKeyedSingleton<IMemory, FlashMemory>(key);
                    break;
                case "database":
                    services.AddKeyedTransient<IMemory, Database>(modelName);
                    services.AddKeyedSingleton<IMemory, Database>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid memory model.");
            }
        }
        #endregion

        #region TTS
        /// <summary>
        /// 根据配置注册TTS（文本转语音）服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="config">小智配置对象</param>
        /// <param name="key">服务键名</param>
        private static void RegisterTts(IServiceCollection services, XiaoZhiConfig config, string key)
        {
            string modelName = ConvertToKebabCase(config.SelectedSettings["TTS"]);
            switch (modelName)
            {
                case "kokoro":
                    services.AddKeyedSingleton<ITts, Kokoro>(key);
                    break;
                case "huoshan-bidirection":
                    services.AddKeyedTransient<ITts, HuoshanBidirectionTTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanBidirectionTTS>(key);
                    break;
                case "huoshan-unidirectional":
                    services.AddKeyedTransient<ITts, HuoshanUnidirectionalTTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanUnidirectionalTTS>(key);
                    break;
                case "huoshan-http":
                    // 注册Flurl客户端缓存用于HTTP TTS服务
                    services.AddSingleton(_ => new FlurlClientCache()
                    .Add(nameof(HuoshanHttpTTS), configure: builder =>
                    {
                        builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                    }));
                    services.AddKeyedTransient<ITts, HuoshanHttpTTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanHttpTTS>(key);
                    break;
                case "huoshan-http-v3":
                    // 注册Flurl客户端缓存用于HTTP V3 TTS服务
                    services.AddSingleton(_ => new FlurlClientCache()
                    .Add(nameof(HuoshanHttpV3TTS), configure: builder =>
                    {
                        builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                    }));
                    services.AddKeyedTransient<ITts, HuoshanHttpV3TTS>(modelName);
                    services.AddKeyedTransient<ITts, HuoshanHttpV3TTS>(key);
                    break;
                default:
                    throw new ModelBuildException("Invalid tts model.");
            }
        }
        #endregion

        #region IoT
        /// <summary>
        /// 注册IoT客户端服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterIoT(IServiceCollection services)
        {
            services.AddTransient<IIoTClient, IoTClient>();
        }

        /// <summary>
        /// 构建并设置会话的IoT客户端
        /// </summary>
        /// <param name="session">目标会话对象</param>
        public void BuildIoT(Session session)
        {
            IIoTClient iotClient = this._serviceProvider.GetRequiredService<IIoTClient>();
            if (!iotClient.Build(session))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildIoT_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetIoTClient(iotClient);
            }
        }
        #endregion

        #region MCP
        /// <summary>
        /// 注册MCP（多客户端协议）相关服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterMCP(IServiceCollection services)
        {
            services.AddKeyedTransient<ISubMcpClient, DeviceMcpClient>(SubMCPClientTypeNames.DeviceMcpClient);
            services.AddKeyedTransient<ISubMcpClient, McpEndpointClient>(SubMCPClientTypeNames.McpEndpointClient);
            services.AddKeyedTransient<ISubMcpClient, ServerMcpClient>(SubMCPClientTypeNames.ServerMcpClient);

        }

        /// <summary>
        /// 构建并设置会话的MCP客户端
        /// </summary>
        /// <param name="session">目标会话对象</param>
        public void BuildMCP(Session session)
        {
            IMcpClient mcpClient = this._serviceProvider.GetRequiredService<IMcpClient>();

            // 构建MCP客户端配置字典
            Dictionary<string, MCPClientBuildConfig> mcpBuildConfigs = new Dictionary<string, MCPClientBuildConfig>();
            if (this._config.McpSettings is null || !this._config.McpSettings.Any())
            {
                mcpBuildConfigs.Add(SubMCPClientTypeNames.DeviceMcpClient, new MCPClientBuildConfig
                (session, new ModelSetting()));
            }
            else
            {
                mcpBuildConfigs = this._config.McpSettings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new MCPClientBuildConfig(session, kvp.Value));
            }
            if (!mcpClient.Build(mcpBuildConfigs))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildMCP_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetMcpClient(mcpClient);
            }
        }
        #endregion

        #region AudioPlayer
        /// <summary>
        /// 注册音频播放器相关服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        private static void RegisterAudioPlayer(IServiceCollection services)
        {
            services.AddTransient<IMusicPlayer, FileMusicPlayer>();
            services.AddTransient<ISystemNotification, NotificationPlayer>();
            services.AddTransient<IAudioPlayerClient, AudioPlayerClient>();
        }

        /// <summary>
        /// 构建并设置会话的音频播放器客户端
        /// </summary>
        /// <param name="session">目标会话对象</param>
        public void BuildAudioPlayer(Session session)
        {
            IAudioPlayerClient audioPlayerClient = this._serviceProvider.GetRequiredService<IAudioPlayerClient>();
            if (!audioPlayerClient.Build(this._config.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioPlayer_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetAudioPlayerClient(audioPlayerClient);
            }
        }
        #endregion
        #region AudioProcessor
        /// <summary>
        /// 注册音频混合器服务，将IAudioProcessor接口映射到DefaultAudioProcessor实现
        /// </summary>
        /// <param name="services">依赖注入服务集合</param>
        private static void RegisterAudioMixer(IServiceCollection services)
        {
            services.AddTransient<IAudioProcessor, DefaultAudioProcessor>();
        }

        /// <summary>
        /// 构建音频处理器并将其设置到会话中
        /// </summary>
        /// <param name="session">当前会话对象</param>
        public void BuildAudioProcessor(Session session)
        {
            IAudioProcessor audioProcessor = this._serviceProvider.GetRequiredService<IAudioProcessor>();
            if (!audioProcessor.Build(this._config.AudioSetting))
            {
                this._logger.LogWarning(Lang.ProviderManager_BuildAudioProcessor_BuildFailed, session.SessionId);
            }
            else
            {
                session.PrivateProvider.SetAudioProcessor(audioProcessor);
            }
        }
        #endregion
        #endregion

        /// <summary>
        /// 注册全局提供者（VAD、ASR、LLM、TTS）到会话中
        /// </summary>
        /// <param name="session">当前会话对象</param>
        /// <returns>注册成功返回true，失败返回false</returns>
        private bool RegisterGlobalProviders(Session session)
        {
            #region Vad
            // 获取并初始化VAD（语音活动检测）提供者
            IVad genericVad = this._serviceProvider.GetRequiredKeyedService<IVad>(GlobalProviderNames.GLOBAL_VAD);
            if (!genericVad.IsSherpaModel && !genericVad.Build(this.GetSelectedSetting("VAD", this._config)))
            {
                this._logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_VadBuildFailed, genericVad.ModelName);
                return false;
            }
            session.PrivateProvider.SetVad(genericVad);
            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_VadInitialized, genericVad.ModelName, session.DeviceId);
            #endregion

            #region Asr
            // 获取并初始化ASR（自动语音识别）提供者
            IAsr genericAsr = this._serviceProvider.GetRequiredKeyedService<IAsr>(GlobalProviderNames.GLOBAL_ASR);
            if (!genericAsr.IsSherpaModel && !genericAsr.Build(this.GetSelectedSetting("ASR", this._config)))
            {
                this._logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_AsrBuildFailed, genericAsr.ModelName);
                return false;
            }
            session.PrivateProvider.SetAsr(genericAsr);
            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_AsrInitialized, genericAsr.ModelName, session.DeviceId);
            #endregion

            #region LLM
            // 克隆全局内核并配置LLM构建参数
            Kernel privateKernel = this._globalKernel.Clone();
            ILlm genericLlm = this._serviceProvider.GetRequiredService<ILlm>();

            ModelSetting emotionLLMModelSetting = this.GetSelectedLLMSetting("EmotionLLM", this._config);
            ModelSetting chatLLMModelSetting = this.GetSelectedLLMSetting("ChatLLM", this._config);
            bool useStreaming = chatLLMModelSetting.Config.GetConfigValueOrDefault("UseStreaming", false);
            bool useEmotions = emotionLLMModelSetting.Config.GetConfigValueOrDefault("UseEmotions", false);

            LLMBuildConfig llmBuildConfig = new LLMBuildConfig(
                emotionLLMModelSetting.ModelName,
                chatLLMModelSetting.ModelName,
                this._config.Prompt,
                useStreaming,
                useEmotions,
                SummaryMemory: string.Empty,
                privateKernel);

            privateKernel.Data.Add("session", session);
            if (!genericLlm.Build(llmBuildConfig))
            {
                throw new ModelBuildException("Failed to build generic LLM model.");
            }
            session.PrivateProvider.SetKernel(privateKernel);
            session.PrivateProvider.SetLlm(genericLlm);

            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_LlmInitialized, emotionLLMModelSetting.ModelName, chatLLMModelSetting.ModelName, session.DeviceId);
            #endregion

            #region Tts
            // 获取并初始化TTS（文本转语音）提供者
            ITts genericTts = this._serviceProvider.GetRequiredKeyedService<ITts>(GlobalProviderNames.GLOBAL_TTS);
            if (!genericTts.IsSherpaModel && !genericTts.Build(this.GetSelectedSetting("TTS", this._config)))
            {
                this._logger.LogError(Lang.ProviderManager_RegisterGlobalProviders_TtsBuildFailed, genericTts.ModelName);
                return false;
            }
            session.PrivateProvider.SetTts(genericTts);
            this._logger.LogInformation(Lang.ProviderManager_RegisterGlobalProviders_TtsInitialized, genericTts.ModelName, session.DeviceId);
            #endregion

            return true;
        }

        /// <summary>
        /// 将驼峰命名法转换为短横线分隔的kebab-case格式
        /// </summary>
        /// <param name="input">输入的字符串</param>
        /// <returns>转换后的kebab-case格式字符串</returns>
        private static string ConvertToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
        }
    }
}