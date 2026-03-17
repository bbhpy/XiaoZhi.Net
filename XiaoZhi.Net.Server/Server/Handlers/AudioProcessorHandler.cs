using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
 /// <summary>
/// 音频处理器处理器，负责处理音频段并将其转换为混合音频包
/// 实现了输入输出处理接口，支持多路音频流处理
/// </summary>
internal class AudioProcessorHandler : BaseHandler, IInHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>, IOutHandler<MixedAudioPacket>
{
    /// <summary>
    /// 输出音频段对象池
    /// </summary>
    private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
    
    /// <summary>
    /// 输出音频段工作流对象池
    /// </summary>
    private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
    
    /// <summary>
    /// 混合音频包对象池
    /// </summary>
    private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
    
    /// <summary>
    /// 混合音频包工作流对象池
    /// </summary>
    private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;

    /// <summary>
    /// 音频处理器实例
    /// </summary>
    private IAudioProcessor? _audioProcessor;

    /// <summary>
    /// 初始化音频处理器处理器实例
    /// </summary>
    /// <param name="outAudioSegmentPool">输出音频段对象池</param>
    /// <param name="outAudioSegmentWorkflowPool">输出音频段工作流对象池</param>
    /// <param name="mixedAudioPacketPool">混合音频包对象池</param>
    /// <param name="mixedAudioPacketWorkflowPool">混合音频包工作流对象池</param>
    /// <param name="config">小智配置</param>
    /// <param name="logger">日志记录器</param>
    public AudioProcessorHandler(ObjectPool<OutAudioSegment> outAudioSegmentPool, ObjectPool<Workflow<OutAudioSegment>> outAudioSegmentWorkflowPool,
        ObjectPool<MixedAudioPacket> mixedAudioPacketPool, ObjectPool<Workflow<MixedAudioPacket>> mixedAudioPacketWorkflowPool,
        XiaoZhiConfig config, ILogger<AudioProcessorHandler> logger) : base(config, logger)
    {
        this._outAudioSegmentPool = outAudioSegmentPool;
        this._outAudioSegmentWorkflowPool = outAudioSegmentWorkflowPool;
        this._mixedAudioPacketPool = mixedAudioPacketPool;
        this._mixedAudioPacketWorkflowPool = mixedAudioPacketWorkflowPool;
    }

    /// <summary>
    /// 获取处理器名称
    /// </summary>
    public override string HandlerName => nameof(AudioProcessorHandler);
    
    /// <summary>
    /// 第一路输入工作流读取器
    /// </summary>
    public ChannelReader<Workflow<OutAudioSegment>> PreviousReader { get; set; } = null!;
    
    /// <summary>
    /// 第二路输入工作流读取器
    /// </summary>
    public ChannelReader<Workflow<OutAudioSegment>> PreviousReader2 { get; set; } = null!;
    
    /// <summary>
    /// 第三路输入工作流读取器
    /// </summary>
    public ChannelReader<Workflow<OutAudioSegment>> PreviousReader3 { get; set; } = null!;
    
    /// <summary>
    /// 输出工作流写入器
    /// </summary>
    public ChannelWriter<Workflow<MixedAudioPacket>> NextWriter { get; set; } = null!;

    /// <summary>
    /// 构建处理器，初始化音频处理器并注册设备
    /// </summary>
    /// <param name="privateProvider">私有提供者</param>
    /// <returns>构建成功返回true，否则返回false</returns>
    public override bool Build(PrivateProvider privateProvider)
    {
        Session session = this.SendOutter.GetSession();
        if (privateProvider.AudioProcessor is null)
        {
            this.Logger.LogError(Lang.AudioProcessorHandler_Build_AudioProcessorNotConfigured, session.DeviceId);
            return false;
        }
        this._audioProcessor = privateProvider.AudioProcessor;
        this._audioProcessor.RegisterDevice(session.DeviceId, session.SessionId);
        this._audioProcessor.OnMixedAudioDataAvailable += this.OnMixedAudioDataAvailable;
        this.RegisterCancellationToken();
        return true;
    }

    /// <summary>
    /// 当处理器令牌改变时的处理方法，清空所有缓冲区
    /// </summary>
    protected override void OnHandlerTokenChanged()
    {
        this._audioProcessor?.ClearAllBuffers();
    }

    /// <summary>
    /// 处理第一路音频流数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task Handle()
    {
        await foreach (var workflow in this.PreviousReader.ReadAllAsync())
        {
            try
            {
                this.Handle(workflow);
            }
            finally
            {
                this._outAudioSegmentPool.Return(workflow.Data);
                this._outAudioSegmentWorkflowPool.Return(workflow);
            }
        }
    }
    
    /// <summary>
    /// 处理第二路音频流数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task Handle2()
    {
        await foreach (var workflow in this.PreviousReader2.ReadAllAsync())
        {
            try
            {
                this.Handle(workflow);
            }
            finally
            {
                this._outAudioSegmentPool.Return(workflow.Data);
                this._outAudioSegmentWorkflowPool.Return(workflow);
            }
        }
    }
    
    /// <summary>
    /// 处理第三路音频流数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task Handle3()
    {
        await foreach (var workflow in this.PreviousReader3.ReadAllAsync())
        {
            try
            {
                this.Handle(workflow);
            }
            finally
            {
                this._outAudioSegmentPool.Return(workflow.Data);
                this._outAudioSegmentWorkflowPool.Return(workflow);
            }
        }
    }

    /// <summary>
    /// 处理单个工作流中的音频段数据
    /// </summary>
    /// <param name="workflow">音频段工作流</param>
    public void Handle(Workflow<OutAudioSegment> workflow)
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

        if (this._audioProcessor is null)
        {
            this.Logger.LogError(Lang.AudioProcessorHandler_Handle_AudioProcessorNotBuilt, session.DeviceId);
            return;
        }

        OutAudioSegment s = workflow.Data;
        try
        {
            this.HandlerToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(s.SentenceId))
            {
                this._audioProcessor.RegisterSubtitle(s.SentenceId, s.AudioType, s.IsFirstFrame ? TtsStatus.SentenceStart : TtsStatus.SentenceEnd, s.Content, s.Emotion);
            }

            this._audioProcessor.ProcessAudio(s.AudioType, s.AudioData, s.Content, s.Emotion, s.IsFirstFrame, s.IsLastFrame, s.SentenceId);

            if (s.IsLastSegment)
            {
                this._audioProcessor.CompleteStream(s.AudioType);
                if (session.CloseAfterChat)
                {
                    this._audioProcessor.CompleteStream(AudioType.Music);
                    this._audioProcessor.CompleteStream(AudioType.SystemNotification);
                    this._audioProcessor.CompleteStream(AudioType.Other);
                }
            }

        }
        catch (OperationCanceledException)
        {
            this.Logger.LogDebug(Lang.AudioProcessorHandler_Handle_Cancelled, session.DeviceId);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, Lang.AudioProcessorHandler_Handle_ProcessFailed, session.DeviceId);
        }
    }

    /// <summary>
    /// 当混合音频数据可用时的回调方法，将混合音频数据包装成工作流并写入输出通道
    /// </summary>
    /// <param name="mixedPcmData">混合PCM数据</param>
    /// <param name="isFirst">是否为第一帧</param>
    /// <param name="isLast">是否为最后一帧</param>
    /// <param name="sentenceId">句子ID</param>
    private async void OnMixedAudioDataAvailable(float[] mixedPcmData, bool isFirst, bool isLast, string? sentenceId)
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
        
        var mixedAudioPacket = this._mixedAudioPacketPool.Get();
        var workflow = this._mixedAudioPacketWorkflowPool.Get();
        
        try
        {
            mixedAudioPacket.Initialize(mixedPcmData, isFirst, isLast, sentenceId);
            workflow.Initialize(session, mixedAudioPacket);
            await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
        }
        catch (OperationCanceledException)
        {
            this._mixedAudioPacketPool.Return(mixedAudioPacket);
            this._mixedAudioPacketWorkflowPool.Return(workflow);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, Lang.AudioProcessorHandler_OnMixedAudioDataAvailable_WriteFailed, session.DeviceId);
            this._mixedAudioPacketPool.Return(mixedAudioPacket);
            this._mixedAudioPacketWorkflowPool.Return(workflow);
        }
    }


    /// <summary>
    /// 释放资源，完成输出写入器
    /// </summary>
    public override void Dispose()
    {
        this.NextWriter.Complete();
        base.Dispose();
    }
}
}