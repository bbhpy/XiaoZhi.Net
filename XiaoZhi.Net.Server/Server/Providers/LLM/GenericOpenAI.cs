using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal class GenericOpenAI : BaseProvider<GenericOpenAI, LLMBuildConfig>, ILlm
    {
        private readonly IChatAgent _chatAgent;
        private readonly IEmotionAgent _emotionAgent;

        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly Dictionary<string, IAgent> _subAgents = new Dictionary<string, IAgent>();
        private Kernel? _kernel;
        private int _seqParagraphId = 0;
        private int _seqSentenceId = 0;

        // ✅ 添加并发锁
        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        private bool _isProcessing = false;
        public GenericOpenAI(IChatAgent chatAgent,
            IEmotionAgent emotionAgent,
            ObjectPool<OutSegment> outSegmentPool,
            ILogger<GenericOpenAI> logger) : base(logger)
        {
            this._chatAgent = chatAgent;
            this._emotionAgent = emotionAgent;

            this._outSegmentPool = outSegmentPool;
            this._subAgents = new Dictionary<string, IAgent>();
            this.LLMChatHistory = new ChatHistory();
        }
        public override string ModelName => nameof(GenericOpenAI);
        public override string ProviderType => "llm";

        public bool UseStreaming { get; private set; }
        public ChatHistory LLMChatHistory { get; }

        public event Action? OnBeforeTokenGenerate;
        public event Action<OutSegment>? OnTokenGenerating;
        public event Action<IEnumerable<OutSegment>>? OnTokenGenerated;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._kernel = modelSetting.Kernel;
                this.UseStreaming = modelSetting.UseStreaming;

                this._subAgents.Add(SubAgentNames.EmotionAgent, this._emotionAgent);
                this._subAgents.Add(SubAgentNames.ChatAgent, this._chatAgent);

                var buildResults = this._subAgents.Values
                    .AsParallel()
                    .Select(client => client.Build(modelSetting))
                    .ToArray();

                return buildResults.All(result => result);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.GenericOpenAI_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }
        public override void RegisterDevice(string deviceId, string sessionId)
        {
            foreach (var agent in this._subAgents.Values)
            {
                agent.RegisterDevice(deviceId, sessionId);
            }
            base.RegisterDevice(deviceId, sessionId);
        }

        public async Task StartDialogueAsync(string userMessage,Session session, CancellationToken token)
        {
            if (session == null)
            {
                this.Logger.LogError("Session 为空，无法处理对话");
                return;
            }

            // ✅ 使用 Session 的锁
            if (!await session.AcquireDialogueLockAsync(0)) // 0 表示不等待，立即返回
            {
                this.Logger.LogWarning("设备 {DeviceId} 已有对话在处理中，跳过本次请求", session.DeviceId);
                return;
            }
            try
            {
                if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
                {
                    throw new SessionNotInitializedException();
                }
                if (!this._subAgents.Any() || this._kernel is null)
                {
                    this.Logger.LogError(Lang.GenericOpenAI_StartDialogueAsync_NotBuilt, this.ProviderType, this.ModelName);
                    return;
                }
                if (session.IsVoiceRecognitionActive())
                {
                    this.Logger.LogDebug("设备 {DeviceId} 语音识别进行中，等待 200ms", session.DeviceId);
                    await Task.Delay(200, token);
                }
                if (this._chatAgent.UseStreaming)
                {
                    await this.ChatByStreamingAsync(userMessage, token);
                }
                else
                {
                    await this.ChatAsync(userMessage, token);
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "设备 {DeviceId} 对话处理异常", session.DeviceId);
            }
            finally
            {
                session.ReleaseDialogueLock();
                this.Logger.LogDebug("设备 {DeviceId} 对话处理完成，释放锁", session.DeviceId);
            }
        }
        protected override string GenerateId()
        {
            string devicePart = this.ReplaceMacDelimiters(this.DeviceId, "_");
            string sessionPart = this.SessionId.Replace("-", string.Empty);
            if (sessionPart.Length > 7)
            {
                sessionPart = sessionPart.Substring(0, 7);
            }
            int sequence = Interlocked.Increment(ref this._seqParagraphId);
            return $"{devicePart}_{sessionPart}_{sequence}";
        }

        private string GenerateSentenceId(string paragraphId) 
        {
            return $"{paragraphId}_{Interlocked.Increment(ref this._seqSentenceId)}";
        }

        private async Task ChatAsync(string userMessage, CancellationToken token)
        {
            List<OutSegment> allResponse = new List<OutSegment>();
            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                string assistantResponse = await this._chatAgent.GenerateChatResponseAsync(userMessage, token);
                token.ThrowIfCancellationRequested();

                // ✅ 添加日志，查看原始响应
                this.Logger.LogDebug("LLM原始响应: {Response}", assistantResponse);

                string cleanContent = DialogueHelper.GetStringNoPunctuationOrEmoji(assistantResponse);

                // ✅ 添加日志，查看清理后的内容
                this.Logger.LogDebug("清理后内容: {CleanContent}", cleanContent);

                IEnumerable<string> segments = DialogueHelper.SplitContentByPunctuations(cleanContent);

                // ✅ 关键修改：去重，并记录重复
                var uniqueSegments = new List<string>();
                var seenSegments = new HashSet<string>(StringComparer.Ordinal);

                foreach (var segment in segments)
                {
                    if (string.IsNullOrWhiteSpace(segment))
                        continue;

                    if (!seenSegments.Contains(segment))
                    {
                        seenSegments.Add(segment);
                        uniqueSegments.Add(segment);
                    }
                    else
                    {
                        this.Logger.LogWarning("检测到重复的句子: '{Segment}'，已跳过", segment);
                    }
                }

                // ✅ 日志：去重后的结果
                this.Logger.LogDebug("去重后句子: {Segments}", string.Join(" | ", uniqueSegments));

                int index = 0;
                int count = uniqueSegments.Count;
                string paragraphId = this.GenerateId();

                foreach (string sentence in uniqueSegments)
                {
                    token.ThrowIfCancellationRequested();
                    index++;
                    Emotion detectedEmotion = await this._emotionAgent.AnalyzeEmotionAsync(userMessage, sentence, token);
                    this.Logger.LogDebug(Lang.GenericOpenAI_ChatAsync_EmotionDetected, detectedEmotion, sentence);

                    var outSegment = this._outSegmentPool.Get();
                    outSegment.Initialize(sentence, index == 1, index == count, detectedEmotion, paragraphId, this.GenerateSentenceId(paragraphId));

                    allResponse.Add(outSegment);
                    this.OnTokenGenerating?.Invoke(outSegment);
                }

                this.OnTokenGenerated?.Invoke(allResponse);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.GenericOpenAI_ChatAsync_Cancelled, allResponse.Count);
                this.OnTokenGenerated?.Invoke(allResponse);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.GenericOpenAI_ChatAsync_UnexpectedError, this.ProviderType);
                this.OnTokenGenerated?.Invoke(allResponse);
            }
        }

        private async Task ChatByStreamingAsync(string userMessage, CancellationToken token)
        {
            List<OutSegment> allResponse = new List<OutSegment>();
            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                string paragraphId = this.GenerateId();

                // 先收集所有句子
                List<string> sentences = new List<string>();
                await foreach (string sentence in this._chatAgent.GenerateChatResponseStreamingAsync(userMessage, token))
                {
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(sentence))
                    {
                        sentences.Add(sentence);
                    }
                }

                // 处理所有句子
                for (int i = 0; i < sentences.Count; i++)
                {
                    string sentence = sentences[i];
                    Emotion detectedEmotion = await this._emotionAgent.AnalyzeEmotionAsync(userMessage, sentence, token);

                    var outSegment = this._outSegmentPool.Get();
                    outSegment.Initialize(sentence, detectedEmotion, paragraphId, this.GenerateSentenceId(paragraphId));

                    // 正确设置标志
                    outSegment.IsFirstSegment = (i == 0);
                    outSegment.IsLastSegment = (i == sentences.Count - 1);

                    allResponse.Add(outSegment);

                    // 现在发送时 IsLastSegment 已经正确设置
                    this.OnTokenGenerating?.Invoke(outSegment);
                    this.Logger.LogDebug("输出片段 {Index}/{Total}: {Content}, IsLast={IsLast}",
                        i + 1, sentences.Count, outSegment.Content, outSegment.IsLastSegment);
                }

                this.OnTokenGenerated?.Invoke(allResponse);
                this.Logger.LogDebug("流式对话完成，共生成 {Count} 个片段", allResponse.Count);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug("流式对话被取消，已生成 {Count} 个片段", allResponse.Count);
                this.OnTokenGenerated?.Invoke(allResponse);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "流式对话异常");
                this.OnTokenGenerated?.Invoke(allResponse);
            }
        }

        public override void Dispose()
        {
            foreach (var agent in this._subAgents.Values)
            {
                agent.Dispose();
            }
            this._subAgents.Clear();
        }
    }
}
