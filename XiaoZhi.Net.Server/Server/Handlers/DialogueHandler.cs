using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    /// <summary>
    /// 对话处理器，负责处理对话流程，实现输入输出处理接口
    /// </summary>
    internal class DialogueHandler : BaseHandler, IInHandler<string, string>, IOutHandler<OutSegment>
    {
        /// <summary>
        /// 字符串工作流对象池
        /// </summary>
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;

        /// <summary>
        /// 输出片段工作流对象池
        /// </summary>
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;

        /// <summary>
        /// 输出片段对象池
        /// </summary>
        private readonly ObjectPool<OutSegment> _outSegmentPool;

        /// <summary>
        /// 大语言模型实例
        /// </summary>
        private ILlm? _llm;

        /// <summary>
        /// 初始化对话处理器实例
        /// </summary>
        /// <param name="stringWorkflowPool">字符串工作流对象池</param>
        /// <param name="outSegmentWorkflowPool">输出片段工作流对象池</param>
        /// <param name="outSegmentPool">输出片段对象池</param>
        /// <param name="config">小智配置</param>
        /// <param name="logger">日志记录器</param>
        public DialogueHandler(ObjectPool<Workflow<string>> stringWorkflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            ObjectPool<OutSegment> outSegmentPool,
            XiaoZhiConfig config,
            ILogger<DialogueHandler> logger) : base(config, logger)
        {
            this._stringWorkflowPool = stringWorkflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
            this._outSegmentPool = outSegmentPool;
        }

        /// <summary>
        /// 获取对话处理器的名称
        /// </summary>
        /// <returns>返回处理器名称"DialogueHandler"</returns>
        public override string HandlerName => nameof(DialogueHandler);

        /// <summary>
        /// 前置工作流读取器，用于读取字符串类型的工作流数据
        /// </summary>
        public ChannelReader<Workflow<string>> PreviousReader { get; set; } = null!;

        /// <summary>
        /// 第二个前置工作流读取器，用于读取字符串类型的工作流数据
        /// </summary>
        public ChannelReader<Workflow<string>> PreviousReader2 { get; set; } = null!;

        /// <summary>
        /// 下游工作流写入器，用于写入输出片段类型的OutSegment工作流数据
        /// </summary>
        public ChannelWriter<Workflow<OutSegment>> NextWriter { get; set; } = null!;

        /// <summary>
        /// 构建处理器，初始化大语言模型并注册相关事件
        /// </summary>
        /// <param name="privateProvider">私有提供者</param>
        /// <returns>构建是否成功</returns>
        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Llm is null)
            {
                this.Logger.LogError(Lang.DialogueHandler_Build_LlmNotConfigured, session.DeviceId);
                return false;
            }

            this._llm = privateProvider.Llm;
            this._llm.OnBeforeTokenGenerate += this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating += this.OnTokenGenerating;
            this._llm.OnTokenGenerated += this.OnTokenGenerated;
            this._llm.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();
            return true;
        }

        /// <summary>
        /// 处理来自第一个读取器的工作流数据
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task Handle()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
                }
                finally
                {
                    this._stringWorkflowPool.Return(workflow);
                }
            }
        }

        /// <summary>
        /// 处理来自第二个读取器的工作流数据
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task Handle2()
        {
            await foreach (var workflow in this.PreviousReader2.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
                }
                finally
                {
                    this._stringWorkflowPool.Return(workflow);
                }
            }
        }

        /// <summary>
        /// 处理单个工作流
        /// </summary>
        /// <param name="workflow">工作流实例</param>
        /// <returns>异步任务</returns>
        public async Task Handle(Workflow<string> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            if (!this.CheckWorkflowValid(workflow))
            {
                return;
            }

            if (this._llm is null)
            {
                this.Logger.LogError(Lang.DialogueHandler_Handle_LlmNotConfigured, session.DeviceId);
                return;
            }

            // 检查设备是否已绑定
            if (!session.IsDeviceBinded)
            {
                var outSegment = this._outSegmentPool.Get();
                var notBindWorkflow = this._outSegmentWorkflowPool.Get();

                outSegment.Initialize("NOT_BIND", true, true, Emotion.Neutral);
                notBindWorkflow.Initialize(session, outSegment);
                await this.NextWriter.WriteAsync(notBindWorkflow);
                return;
            }

            try
            {
                using (CodeTimer timer = CodeTimer.Create(Lang.DialogueHandler_Handle_LlmCallTime, this.Logger))
                {
                    await this._llm.StartDialogueAsync(workflow.Data,session, this.HandlerToken);
                }
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.DialogueHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DialogueHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        /// <summary>
        /// 无语音时关闭连接的处理方法
        /// </summary>
        /// <param name="workflow">工作流实例</param>
        /// <returns>异步任务</returns>
        public async void NoVoiceCloseConnect(Workflow<string> workflow)
        {
            await this.Handle(workflow);
        }

        /// <summary>
        /// 令牌生成前的回调处理
        /// 发送LLM思考消息和STT思考消息
        /// </summary>
        private void OnBeforeTokenGenerate()
        {
            this.SendOutter.SendLlmMessageAsync(Emotion.Thinking);
            this.SendOutter.SendSttMessageAsync(Lang.DialogueHandler_OnBeforeTokenGenerate_Thinking);
        }

        /// <summary>
        /// 令牌生成中的回调处理
        /// 将输出片段克隆并写入下一个处理器
        /// </summary>
        /// <param name="outSegment">输出片段</param>
        private async void OnTokenGenerating(OutSegment outSegment)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            var clonedSegment = this._outSegmentPool.Get();
            clonedSegment.Initialize(outSegment.Content, outSegment.IsFirstSegment, outSegment.IsLastSegment, outSegment.Emotion, outSegment.ParagraphId, outSegment.SentenceId);

            var workflow = this._outSegmentWorkflowPool.Get();
            workflow.Initialize(session, clonedSegment);

            try
            {
                await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outSegmentPool.Return(clonedSegment);
                this._outSegmentWorkflowPool.Return(workflow);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DialogueHandler_OnTokenGenerating_WriteFailed, session.DeviceId);
                this._outSegmentPool.Return(clonedSegment);
                this._outSegmentWorkflowPool.Return(workflow);
            }
        }

        /// <summary>
        /// 令牌生成完成后的回调处理
        /// 记录响应文本并将所有输出片段返回到对象池
        /// </summary>
        /// <param name="outSegments">输出片段集合</param>
        private void OnTokenGenerated(IEnumerable<OutSegment> outSegments)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                // 即使取消了也需要将片段返回到池中
                foreach (var seg in outSegments)
                {
                    this._outSegmentPool.Return(seg);
                }
                return;
            }

            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                foreach (var seg in outSegments)
                {
                    this._outSegmentPool.Return(seg);
                }
                return;
            }

            this.Logger.LogDebug(Lang.DialogueHandler_OnTokenGenerated_ResponseText, string.Join(string.Empty, outSegments.Select(o => o.Content)));
            foreach (var seg in outSegments)
            {
                this._outSegmentPool.Return(seg);
            }
        }

        /// <summary>
        /// 释放资源，注销大语言模型事件并完成写入器
        /// </summary>
        public override void Dispose()
        {
            if (this._llm is not null)
            {
                this._llm.OnBeforeTokenGenerate -= this.OnBeforeTokenGenerate;
                this._llm.OnTokenGenerating -= this.OnTokenGenerating;
                this._llm.OnTokenGenerated -= this.OnTokenGenerated;
            }
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}