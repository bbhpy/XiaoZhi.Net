using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>, IChatAgent
    {
        private Kernel? _kernel;
        private IChatCompletionService? _chatAgentService;
        private OpenAIPromptExecutionSettings? _chatExecutionSettings;

        public ChatAgent(IServiceProvider serviceProvider, ILogger<ChatAgent> logger) : base(serviceProvider, logger)
        {
        }

        public bool UseStreaming { get; private set; }
        public override string ModelName => nameof(ChatAgent);
        public override int Order => 10;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._kernel = modelSetting.Kernel;
                this.UseStreaming = modelSetting.UseStreaming;
                this._chatAgentService = this.ServiceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{modelSetting.ChatLLMModelName}");
                this.Prompt = modelSetting.Prompt;

                // 检查是否有任何插件
                bool hasFunctions = false;
                if (this._kernel != null)
                {
                    hasFunctions = this._kernel.Plugins.Any();
                    this.Logger.LogDebug("检测到 {Count} 个插件: {Plugins}",
                        this._kernel.Plugins.Count,
                        string.Join(", ", this._kernel.Plugins.Select(p => p.Name)));
                }

                // 如果有插件，启用函数调用；否则禁用
                this._chatExecutionSettings = CreateExecutionSettings(hasFunctions);

                if (!string.IsNullOrEmpty(modelSetting.SummaryMemory))
                {
                    this.ChatHistory.AddSystemMessage(modelSetting.SummaryMemory);
                }

                bool pluginsBuildResult = this.BuildPlugins(modelSetting.Kernel);

                if (pluginsBuildResult)
                {
                    this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
                    this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                    return true;
                }
                else
                {
                    this.Logger.LogError(Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                    this.Logger.LogError(Lang.ChatAgent_Build_BuildPluginsFailed, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                return false;
            }
        }

        public override void RegisterDevice(string deviceId, string sessionId)
        {
            if (this._kernel is not null)
            {
                foreach (var item in this._kernel.Plugins)
                {
                    if (item is ILLMPlugin llmPlugin)
                    {
                        llmPlugin.RegisterDevice(deviceId, sessionId);
                        this.Logger.LogInformation(Lang.ChatAgent_RegisterDevice_PluginRegistered, llmPlugin.ModelName, deviceId);
                    }
                }
            }
            base.RegisterDevice(deviceId, sessionId);
        }

        public async Task<string> GenerateChatResponseAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatAgentService is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }
            if (this._chatExecutionSettings is null)
            {
                throw new InvalidOperationException("Chat execution settings not initialized");
            }

            this.ChatHistory.AddUserMessage(userMessage);

            // 最多尝试3次，防止无限循环
            int maxRetries = 3;
            string finalContent = string.Empty; 
            List<string> responseHistory = new List<string>();

            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (retry > 0)
                {
                    await Task.Delay(100, token);
                }
                var clientResult = await this._chatAgentService.GetChatMessageContentAsync(
                    this.ChatHistory,
                    this._chatExecutionSettings,
                    this._kernel,
                    token);

                // 检查是否有函数调用
                var functionCalls = clientResult.Items?.OfType<FunctionCallContent>().ToList();

                if (functionCalls != null && functionCalls.Any())
                {
                    this.Logger.LogInformation("检测到 {Count} 个函数调用，第 {Retry} 次尝试",
                        functionCalls.Count, retry + 1);

                    // 函数调用会被自动添加到 ChatHistory 中
                    // 继续下一次循环，让LLM基于函数结果生成响应
                    continue;
                }
                // 获取当前内容
                string currentContent = clientResult.Content ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(currentContent) && responseHistory.Contains(currentContent))
                {
                    this.Logger.LogWarning("检测到重复响应内容，跳过本次处理");
                    continue;
                }

                finalContent = clientResult.Content ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(finalContent))
                {
                    responseHistory.Add(finalContent);
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(finalContent))
            {
                this.Logger.LogError("经过 {MaxRetries} 次尝试后，LLM仍返回空内容", maxRetries);
                return string.Empty;
            }

            string assistantContent = ProcessAssistantContent(finalContent);

            var lastAssistantMessage = this.ChatHistory
                .Where(m => m.Role == AuthorRole.Assistant)
                .LastOrDefault();

            if (lastAssistantMessage != null && lastAssistantMessage.Content == assistantContent)
            {
                this.Logger.LogWarning("检测到重复的助手消息，跳过添加到历史");
            }
            else
            {
                this.ChatHistory.AddAssistantMessage(assistantContent);
                this.Logger.LogDebug("已添加助手响应到对话历史，长度: {Length}", assistantContent.Length);
            }

            return assistantContent;

        }

        // 添加内容处理方法
        private string ProcessAssistantContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            string processed = Regex.Unescape(content);

            // 移除 think 标签
            if (processed.Contains("<think>"))
            {
                processed = Regex.Replace(processed, @"<think>.*?</think>", string.Empty, RegexOptions.Singleline);
            }

            // 清理 Markdown
            processed = MarkdownCleaner.CleanMarkdown(processed);

            return processed;
        }

        public async IAsyncEnumerable<string> GenerateChatResponseStreamingAsync(string userMessage, [EnumeratorCancellation] CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatAgentService is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }

            this.ChatHistory.AddUserMessage(userMessage);

            var executionSettings = this._chatExecutionSettings;
            if (executionSettings == null)
            {
                executionSettings = CreateExecutionSettings(true);
            }

            StringBuilder fullResponse = new StringBuilder();
            StringBuilder currentSentence = new StringBuilder();
            bool hasFunctionCall = false;

            await foreach (var item in this._chatAgentService.GetStreamingChatMessageContentsAsync(
                this.ChatHistory, executionSettings, this._kernel, token))
            {
                token.ThrowIfCancellationRequested();

                // 检查函数调用
                if (item.Items?.Any(i => i is FunctionCallContent) == true)
                {
                    hasFunctionCall = true;
                    var functionCalls = item.Items.OfType<FunctionCallContent>();
                    foreach (var functionCall in functionCalls)
                    {
                        this.Logger.LogInformation(
                            "检测到函数调用: {PluginName}.{FunctionName}",
                            functionCall.PluginName ?? "unknown",
                            functionCall.FunctionName);
                    }
                    // 函数调用会被 AutoInvoke 自动处理，继续等待后续文本响应
                    continue;
                }

                string content = item.Content ?? string.Empty;
                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                string text = MarkdownCleaner.CleanMarkdown(Regex.Unescape(content));

                fullResponse.Append(text);
                currentSentence.Append(text);

                string currentText = currentSentence.ToString();

                // 使用完整句子匹配（不包含逗号）
                Match match = DialogueHelper.SENTENCE_SPLIT_REGEX_FULL.Match(currentText);

                while (match.Success)
                {
                    int splitPosition = match.Index + match.Length;
                    string sentence = currentText.Substring(0, splitPosition).Trim();

                    if (!string.IsNullOrEmpty(sentence))
                    {
                        this.Logger.LogDebug("流式输出完整句子: {Sentence}", sentence);
                        yield return sentence;
                    }

                    string remaining = currentText.Substring(splitPosition);
                    currentSentence.Clear();
                    currentSentence.Append(remaining);
                    currentText = remaining;
                    match = DialogueHelper.SENTENCE_SPLIT_REGEX_FULL.Match(currentText);
                }
            }

            // 处理最后剩余的内容（没有标点结尾）
            if (currentSentence.Length > 0)
            {
                string remaining = currentSentence.ToString().Trim();
                if (!string.IsNullOrEmpty(remaining))
                {
                    this.Logger.LogDebug("流式输出剩余内容: {Remaining}", remaining);
                    yield return remaining;
                }
            }

            string allContent = fullResponse.ToString().Trim();

            if (hasFunctionCall && string.IsNullOrEmpty(allContent))
            {
                this.Logger.LogDebug("函数调用后无文本响应，已自动处理");
            }
            else if (!string.IsNullOrEmpty(allContent))
            {
                this.ChatHistory.AddAssistantMessage(allContent);
                this.Logger.LogDebug("已添加助手响应到对话历史，长度: {Length}", allContent.Length);
            }
        }
        private bool BuildPlugins(Kernel kernel)
        {
            #region LocalMusicPlayer
            MusicPlayer musicPlayerPlugin = this.ServiceProvider.GetRequiredService<MusicPlayer>();

            LLMPluginConfig llmPluginConfig = new LLMPluginConfig(kernel);

            if (musicPlayerPlugin.Build(llmPluginConfig))
            {
                string pluginName = musicPlayerPlugin.ModelName;
                kernel.ImportPluginFromObject(musicPlayerPlugin, pluginName);
                // ✅ 添加日志确认插件和函数已注册
                var functions = kernel.Plugins.GetFunctionsMetadata();
                var musicPlayerFunctions = functions.Where(f => f.PluginName == pluginName).ToList();

                this.Logger.LogInformation(
                    "成功注册插件 {PluginName}，包含 {FunctionCount} 个函数",
                    pluginName,
                    musicPlayerFunctions.Count);

                foreach (var func in musicPlayerFunctions)
                {
                    this.Logger.LogDebug("已注册函数: {PluginName}.{FunctionName}",
                        func.PluginName, func.Name);
                }
            }
            else
            {
                return false;
            }
            #endregion

            return true;
        }
        private OpenAIPromptExecutionSettings CreateExecutionSettings(bool enableFunctionCalling = true, int maxTokens = 80)
        {
            var baseSettings = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.5f,
                MaxTokens = maxTokens,
                ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                ChatSystemPrompt = this.Prompt
            };

            if (enableFunctionCalling)
            {
                // 关键：显式设置 autoInvoke = true
                baseSettings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                    autoInvoke: true,  // 这个参数让函数自动执行
                    options: new FunctionChoiceBehaviorOptions
                    {
                        AllowParallelCalls = true,
                        AllowConcurrentInvocation = true
                    });

                this.Logger.LogDebug("函数调用已启用，AutoInvoke = true");
            }
            else
            {
                baseSettings.FunctionChoiceBehavior = null;
                this.Logger.LogDebug("函数调用已禁用");
            }

            return baseSettings;
        }
        public override void Dispose()
        {
        }
    }
}
