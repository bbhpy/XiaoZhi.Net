using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
/// <summary>
/// 音频发送处理器，负责处理混合音频数据包的编码和发送
/// </summary>
internal class AudioSendHandler : BaseHandler, IInHandler<MixedAudioPacket>
{
    private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
    private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;

    private IAudioProcessor? _audioProcessor;
    private IAudioEncoder? _audioEncoder;

    /// <summary>
    /// 初始化音频发送处理器实例
    /// </summary>
    /// <param name="mixedAudioPacketPool">混合音频数据包对象池</param>
    /// <param name="mixedAudioPacketWorkflowPool">混合音频工作流对象池</param>
    /// <param name="config">小智配置对象</param>
    /// <param name="logger">日志记录器</param>
    public AudioSendHandler(ObjectPool<MixedAudioPacket> mixedAudioPacketPool, ObjectPool<Workflow<MixedAudioPacket>> mixedAudioPacketWorkflowPool, XiaoZhiConfig config, ILogger<AudioSendHandler> logger) : base(config, logger)
    {
        this._mixedAudioPacketPool = mixedAudioPacketPool;
        this._mixedAudioPacketWorkflowPool = mixedAudioPacketWorkflowPool;

    }
    public override string HandlerName => nameof(AudioSendHandler);

    public ChannelReader<Workflow<MixedAudioPacket>> PreviousReader { get; set; } = null!;

    /// <summary>
    /// 构建处理器，初始化音频处理器和编码器
    /// </summary>
    /// <param name="privateProvider">私有提供者，包含音频处理器和编码器</param>
    /// <returns>构建成功返回true，否则返回false</returns>
    public override bool Build(PrivateProvider privateProvider)
    {
        Session session = this.SendOutter.GetSession();

        if (privateProvider.AudioProcessor is null)
        {
            this.Logger.LogError(Lang.AudioSendHandler_Build_AudioProcessorNotConfigured, session.DeviceId);
            return false;
        }

        if (privateProvider.AudioEncoder is null)
        {
            this.Logger.LogError(Lang.AudioSendHandler_Build_AudioEncoderNotConfigured, session.DeviceId);
            return false;
        }

        this._audioProcessor = privateProvider.AudioProcessor;
        this._audioEncoder = privateProvider.AudioEncoder;
        this.RegisterCancellationToken();
        return true;
    }

    /// <summary>
    /// 处理来自前一个读取器的所有工作流数据
    /// </summary>
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
                this._mixedAudioPacketPool.Return(workflow.Data);
                this._mixedAudioPacketWorkflowPool.Return(workflow);
            }
        }
    }

    /// <summary>
    /// 处理单个工作流中的混合音频数据包
    /// </summary>
    /// <param name="workflow">包含混合音频数据包的工作流</param>
    public async Task Handle(Workflow<MixedAudioPacket> workflow)
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
            this.Logger.LogError(Lang.AudioSendHandler_Handle_AudioProcessorNotConfigured, session.DeviceId);
            return;
        }
        if (this._audioEncoder is null)
        {
            this.Logger.LogError(Lang.AudioSendHandler_Handle_AudioEncoderNotConfigured, session.DeviceId);
            return;
        }
        try
        {
            MixedAudioPacket audioPacket = workflow.Data;

            // 检查并发送字幕相关消息
            if (!string.IsNullOrEmpty(audioPacket.SentenceId) && this._audioProcessor.GetSubtitle(audioPacket.SentenceId, out AudioSubtitle subtitle))
            {
                await this.SendOutter.SendTtsMessageAsync(subtitle.TtsStatus, subtitle.SubtitleText);
                await this.SendOutter.SendLlmMessageAsync(subtitle.Emotion);
            }

            // 处理首帧音频数据
            if (audioPacket.IsFirstFrame)
            {
                await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                this.Logger.LogDebug(Lang.AudioSendHandler_Handle_FirstFrame, session.DeviceId);
            }

            // 编码并发送音频数据
            if (audioPacket.Data is not null && audioPacket.Data.Length > 0)
            {
                byte[] opusData = await this._audioEncoder.EncodeAsync(audioPacket.Data, this.HandlerToken);
                await this.SendOutter.SendAsync(opusData);
            }

            // 处理末帧音频数据
            if (audioPacket.IsLastFrame)
            {
                await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                this.Logger.LogDebug(Lang.AudioSendHandler_Handle_LastFrame, session.DeviceId);
                if (session.CloseAfterChat)
                {
                    await this.SendOutter.CloseSessionAsync("Close Chat");
                }
            }
        }
        catch (OperationCanceledException)
        {
            this.Logger.LogDebug(Lang.AudioSendHandler_Handle_Cancelled, session.DeviceId);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, Lang.AudioSendHandler_Handle_ProcessFailed, session.DeviceId);
        }
    }

}
}

