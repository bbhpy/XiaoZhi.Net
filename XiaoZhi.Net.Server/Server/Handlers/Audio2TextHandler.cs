using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Handlers
{
  /// <summary>
/// 音频转文本处理器，负责将音频数据转换为文本
/// </summary>
internal class Audio2TextHandler : BaseHandler, IInHandler<float[]>, IOutHandler<string>, IAsrEventCallback
{
    /// <summary>
    /// 音频缓冲工作流对象池
    /// </summary>
    private readonly ObjectPool<Workflow<float[]>> _audioBufferWorkflowPool;
    
    /// <summary>
    /// 字符串工作流对象池
    /// </summary>
    private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
    
    /// <summary>
    /// 自动语音识别接口实例
    /// </summary>
    private IAsr? _asr;

    /// <summary>
    /// 初始化音频转文本处理器
    /// </summary>
    /// <param name="circularBufferWorkflowPool">音频缓冲工作流对象池</param>
    /// <param name="stringWorkflowPool">字符串工作流对象池</param>
    /// <param name="config">小知配置</param>
    /// <param name="logger">日志记录器</param>
    public Audio2TextHandler(ObjectPool<Workflow<float[]>> circularBufferWorkflowPool,
        ObjectPool<Workflow<string>> stringWorkflowPool,
        XiaoZhiConfig config,
        ILogger<Audio2TextHandler> logger) : base(config, logger)
    {
        this._audioBufferWorkflowPool = circularBufferWorkflowPool;
        this._stringWorkflowPool = stringWorkflowPool;
    }

    /// <summary>
    /// 获取处理器名称
    /// </summary>
    public override string HandlerName => nameof(Audio2TextHandler);
    
    /// <summary>
    /// 前置处理器读取器
    /// </summary>
    public ChannelReader<Workflow<float[]>> PreviousReader { get; set; } = null!;
    
    /// <summary>
    /// 后置处理器写入器
    /// </summary>
    public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

    /// <summary>
    /// 构建处理器
    /// </summary>
    /// <param name="privateProvider">私有提供者</param>
    /// <returns>构建是否成功</returns>
    public override bool Build(PrivateProvider privateProvider)
    {
        Session session = this.SendOutter.GetSession();
        if (privateProvider.Asr is null)
        {
            this.Logger.LogError(Lang.Audio2TextHandler_Build_AsrNotConfigured, session.DeviceId);
            return false;
        }
        this._asr = privateProvider.Asr;
        this._asr.RegisterDevice(session.DeviceId, session.SessionId, this);
        this.RegisterCancellationToken();
        return true;
    }

    /// <summary>
    /// 处理音频数据
    /// </summary>
    /// <returns></returns>
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
                this._audioBufferWorkflowPool.Return(workflow);
            }
        }
    }

    /// <summary>
    /// 处理单个工作流中的音频数据
    /// </summary>
    /// <param name="workflow">音频工作流</param>
    /// <returns></returns>
    public async Task Handle(Workflow<float[]> workflow)
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

        if (this._asr is null)
        {
            this.Logger.LogError(Lang.Audio2TextHandler_Build_AsrNotConfigured, session.DeviceId);
            return;
        }
        try
        {
            // 检查设备是否已绑定
            if (!session.IsDeviceBinded)
            {
                var notBindWorkflow = this._stringWorkflowPool.Get();
                notBindWorkflow.Initialize(session, "NOT_BIND");
                try
                {
                    await this.NextWriter.WriteAsync(notBindWorkflow, this.HandlerToken);
                }
                catch (OperationCanceledException)
                {
                    this._stringWorkflowPool.Return(notBindWorkflow);
                }
                return;
            }

            await this._asr.ConvertSpeechTextAsync(workflow, session.AudioSetting.SampleRate, session.AudioSetting.FrameSize, this.HandlerToken);
        }
        catch (OperationCanceledException)
        {
            this.Logger.LogDebug(Lang.Audio2TextHandler_Handle_Cancelled, session.DeviceId);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, Lang.Audio2TextHandler_Handle_ProcessFailed, session.DeviceId);
        }
    }

    /// <summary>
    /// 语音文本转换完成回调
    /// </summary>
    /// <param name="success">转换是否成功</param>
    /// <param name="speechText">转换后的文本</param>
    public async void OnSpeechTextConverted(bool success, string speechText)
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
        
        if (!success)
        {
            this.Logger.LogError(Lang.Audio2TextHandler_OnSpeechTextConverted_ConvertFailed);
            return;
        }
        
        // 检查转换结果是否为空或仅包含标点符号和表情符号
        if (string.IsNullOrEmpty(speechText) || string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
        {
            session.Reset();
            this.Logger.LogDebug(Lang.Audio2TextHandler_OnSpeechTextConverted_NoSpeak, session.DeviceId);
            return;
        }

        await this.SendOutter.SendSttMessageAsync(speechText);
        this.Logger.LogDebug(Lang.Audio2TextHandler_OnSpeechTextConverted_SpeakText, session.DeviceId, speechText);

        var nextWorkflow = this._stringWorkflowPool.Get();
        nextWorkflow.Initialize(session, speechText);
        
        try
        {
            await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
        }
        catch (OperationCanceledException)
        {
            this._stringWorkflowPool.Return(nextWorkflow);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        Session session = this.SendOutter.GetSession();
        if (session is null)
        {
            return;
        }
        this._asr?.UnregisterDevice(session.DeviceId, session.SessionId);
        this.NextWriter.Complete();
        base.Dispose();
    }
}
}
