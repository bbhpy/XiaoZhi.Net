using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
/// <summary>
/// 基础语音识别服务抽象类，继承自BaseProvider
/// </summary>
/// <typeparam name="TLogger">日志记录器类型</typeparam>
internal abstract class BaseSherpaAsr<TLogger> : BaseProvider<TLogger, ModelSetting>
{
    /// <summary>
    /// 最大等待时间（毫秒）
    /// </summary>
    private const int MAX_WAITING_TIME_MS = 100;

    /// <summary>
    /// 音频编辑器实例
    /// </summary>
    private readonly IAudioEditor _audioEditor;
    
    /// <summary>
    /// ASR会话字典，存储设备ID到回调接口的映射
    /// </summary>
    private readonly ConcurrentDictionary<string, IAsrEventCallback> _asrSessions;
    
    /// <summary>
    /// 请求通道，用于异步处理ASR请求
    /// </summary>
    private readonly Channel<AsrRequest> _requestChannel;
    
    /// <summary>
    /// 关闭取消令牌源
    /// </summary>
    private readonly CancellationTokenSource _shutdownCts;

    /// <summary>
    /// 离线识别器实例
    /// </summary>
    private OfflineRecognizer? _offlineRecognizer;

    /// <summary>
    /// 后台处理任务
    /// </summary>
    private Task? _backgroudProcessingTask;
        
    /// <summary>
    /// 线程安全锁（保护批处理和Recognizer访问）
    /// </summary>
    private readonly object _recognizerLock = new object();
    /// <summary>
    /// 初始化BaseSherpaAsr实例
    /// </summary>
    /// <param name="audioEditor">音频编辑器</param>
    /// <param name="logger">日志记录器</param>
    protected BaseSherpaAsr(IAudioEditor audioEditor, ILogger<TLogger> logger) : base(logger)
    {
        this._audioEditor = audioEditor;
        this._asrSessions = new ConcurrentDictionary<string, IAsrEventCallback>();
        this._requestChannel = Channel.CreateUnbounded<AsrRequest>(new UnboundedChannelOptions
        {
            SingleReader = true, // 关键：确保只有一个后台线程读取通道，避免并发
            SingleWriter = false
        });
            this._shutdownCts = new CancellationTokenSource();   

        }

        /// <summary>
        /// 获取或设置最大批处理大小
        /// </summary>
        public int MaxBatchSize { get; protected set; } = 50;
    
    /// <summary>
    /// 获取或设置音频保存配置
    /// </summary>
    public AudioSavingConfig? AudioSavingConfig { get; protected set; }
    
    /// <summary>
    /// 获取提供者类型
    /// </summary>
    public override string ProviderType => "asr";

    /// <summary>
    /// 构建离线识别器配置
    /// </summary>
    /// <param name="offlineRecognizerConfig">离线识别器配置</param>
    /// <param name="modelSetting">模型设置</param>
    protected void Build(OfflineRecognizerConfig offlineRecognizerConfig, ModelSetting modelSetting)
    {
        offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");

        string? hotwordsFile = modelSetting.Config.GetConfigValueOrDefault("HotwordsFile");
        if (!string.IsNullOrEmpty(hotwordsFile))
        {
            offlineRecognizerConfig.HotwordsFile = Path.Combine(this.ModelFileFoler, hotwordsFile);
            offlineRecognizerConfig.HotwordsScore = modelSetting.Config.GetConfigValueOrDefault("HotwordsScore", 1.5F);
            offlineRecognizerConfig.DecodingMethod = "modified_beam_search";
            offlineRecognizerConfig.MaxActivePaths = modelSetting.Config.GetConfigValueOrDefault("MaxActivePaths", 4);
        }
        else
        {
            offlineRecognizerConfig.DecodingMethod = "greedy_search";
        }
        //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

        this.MaxBatchSize = modelSetting.Config.GetConfigValueOrDefault("MaxBatchSize", 50);
        this.AudioSavingConfig = modelSetting.Config.GetConfigValueOrDefault("FileSavingOption", new AudioSavingConfig(false));
        
        // 检查并创建音频保存目录
        if (this.AudioSavingConfig.SaveFile && !Directory.Exists(this.AudioSavingConfig.SavePath))
        {
            Directory.CreateDirectory(this.AudioSavingConfig.SavePath);
        }
        this._offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
        this._backgroudProcessingTask = Task.Run(this.Processing);
    }

    /// <summary>
    /// 注册设备到ASR会话
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sessionId">会话ID</param>
    /// <param name="callback">ASR事件回调接口</param>
    public void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback)
    {
        this._asrSessions.AddOrUpdate(deviceId, callback, (_, _) => callback);
        this.Logger.LogDebug(Lang.BaseSherpaAsr_RegisterDevice_Registered, deviceId, sessionId);
    }

    /// <summary>
    /// 取消注册设备
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sessionId">会话ID</param>
    public override void UnregisterDevice(string deviceId, string sessionId)
    {
        if (this._asrSessions.TryRemove(deviceId, out _))
        {
            this.Logger.LogDebug(Lang.BaseSherpaAsr_UnregisterDevice_Unregistered, deviceId, sessionId);
        }
    }

    /// <summary>
    /// 检查设备是否已注册
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>如果设备已注册则返回true，否则返回false</returns>
    public override bool CheckDeviceRegistered(string deviceId, string sessionId)
    {
        return this._asrSessions.ContainsKey(deviceId);
    }

    /// <summary>
    /// 异步转换语音为文本
    /// </summary>
    /// <param name="workflow">工作流对象，包含音频数据</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="frameSize">帧大小</param>
    /// <param name="token">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token)
    {
        if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
        {
            throw new SessionNotInitializedException();
        }
        if (this._offlineRecognizer == null)
        {
            throw new ArgumentNullException(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt);
        }

        if (this._asrSessions.TryGetValue(workflow.DeviceId, out var callback))
        {
            OfflineStream? offlineStream = null;
            try
            {
                // 保存音频文件到指定路径
                if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile)
                {
                    string fileName = this.GenerateAudioFileName(workflow);
                    string filePath = Path.Combine(this.AudioSavingConfig.SavePath, $"{this.ProviderType}_{fileName}.{this.AudioSavingConfig.Format}");

                    bool userSpeechFileSavingResult = await this._audioEditor.SaveAudioFileAsync(filePath, workflow.Data);
                    if (userSpeechFileSavingResult)
                    {
                        this.Logger.LogDebug(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_AudioSaved, fileName);
                    }
                    else
                    {
                        this.Logger.LogWarning(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_AudioNotSaved, fileName);
                    }
                }

                offlineStream = this._offlineRecognizer.CreateStream();
                offlineStream.AcceptWaveform(sampleRate, workflow.Data);

                AsrRequest asrRequest = new AsrRequest(workflow.SessionId, workflow.DeviceId, offlineStream, sampleRate, frameSize, callback, token);

                await this._requestChannel.Writer.WriteAsync(asrRequest, token);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_RequestCancelled, workflow.DeviceId);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseSherpaAsr_ConvertSpeechTextAsync_UnexpectedError, this.ProviderType);
                throw;
            }
            finally
            {
                //offlineStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// 生成音频文件名
    /// </summary>
    /// <typeparam name="T">工作流数据类型</typeparam>
    /// <param name="workflow">工作流对象</param>
    /// <returns>生成的音频文件名</returns>
    private string GenerateAudioFileName<T>(Workflow<T> workflow)
    {
        string devicePart = this.ReplaceMacDelimiters(workflow.DeviceId, "_");
        string sessionPart = workflow.SessionId.Replace("-", string.Empty);
        if (sessionPart.Length > 7)
        {
            sessionPart = sessionPart.Substring(0, 7);
        }
        return $"{devicePart}_{sessionPart}_{workflow.TurnId}";
    }

    /// <summary>
    /// 处理ASR请求的后台任务
    /// </summary>
    /// <returns>异步任务</returns>
    private async Task Processing()
    {
        if (this._offlineRecognizer == null)
        {
            throw new ArgumentNullException(Lang.BaseSherpaAsr_Processing_ProviderNotBuilt);
        }
        CancellationToken shutDownToken = this._shutdownCts.Token;

        while (!shutDownToken.IsCancellationRequested)
        {
            try
            {
                if (!await this._requestChannel.Reader.WaitToReadAsync(shutDownToken))
                {
                    break;
                }

                var batchRequests = new List<AsrRequest>(this.MaxBatchSize);

                if (this._requestChannel.Reader.TryRead(out var firstRequest))
                {
                    batchRequests.Add(firstRequest);
                }

                if (this.MaxBatchSize > 1)
                {
                    while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out var req))
                    {
                        batchRequests.Add(req);
                    }

                    // 等待更多请求以达到最大批处理大小或超时
                    if (batchRequests.Count < this.MaxBatchSize)
                    {
                        using var timeoutCts = new CancellationTokenSource(MAX_WAITING_TIME_MS);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutDownToken, timeoutCts.Token);

                        try
                        {
                            while (batchRequests.Count < this.MaxBatchSize)
                            {
                                if (await this._requestChannel.Reader.WaitToReadAsync(linkedCts.Token))
                                {
                                    while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out var req))
                                    {
                                        batchRequests.Add(req);
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // 忽略超时，但尊重关闭信号
                            if (shutDownToken.IsCancellationRequested) break;
                        }
                    }
                }

                if (batchRequests.Count > 0)
                {
                    // 过滤已取消的请求
                    var validRequests = new List<AsrRequest>(batchRequests.Count);
                    foreach (var request in batchRequests)
                    {
                        if (request.Token.IsCancellationRequested)
                        {
                            request.Stream.Dispose();
                            this.Logger.LogDebug(Lang.BaseSherpaAsr_Processing_RequestCancelledBeforeProcessing, request.DeviceId);
                            continue;
                        }
                        validRequests.Add(request);
                    }


                    if (validRequests.Count > 0)
                    {
                        await Task.Run(() =>
                        {
                            this._offlineRecognizer.Decode(validRequests.Select(b => b.Stream));
                        });

                        foreach (AsrRequest request in validRequests)
                        {
                            try
                            {
                                if (request.Token.IsCancellationRequested)
                                {
                                    this.Logger.LogDebug(Lang.BaseSherpaAsr_Processing_RequestCancelledAfterDecoding, request.DeviceId);
                                }
                                else
                                {
                                    string resultText = request.Stream.Result.Text;
                                    request.Callback.OnSpeechTextConverted(true, resultText);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.Logger.LogError(ex, Lang.BaseSherpaAsr_Processing_ResultProcessingError, request.DeviceId);
                            }
                            finally
                            {
                                request.Stream.Dispose();
                            }
                        }

                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseSherpaAsr_Processing_ErrorLoop);
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        this._shutdownCts.Cancel();

        try
        {
            this._backgroudProcessingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            this.Logger.LogError(Lang.BaseSherpaAsr_Dispose_WaitFailed);
        }

        this._offlineRecognizer?.Dispose();
        this._shutdownCts.Dispose();
    }

}
}
