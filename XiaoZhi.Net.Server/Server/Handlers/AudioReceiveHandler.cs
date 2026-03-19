using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.VAD;

namespace XiaoZhi.Net.Server.Handlers
{
    /// <summary>
    /// 音频接收处理器 ，负责接收客户端发送的音频数据，进行解码和语音活动检测，并将处理后的音频数据传递给下一个处理器
    /// </summary>
    internal class AudioReceiveHandler : BaseHandler, IOutHandler<float[]>, IVadEventCallback
    {
        /// <summary>
        /// 音频数据缓存工作流池
        /// </summary>
        private readonly ObjectPool<Workflow<float[]>> _audioBufferWorkflowPool;
        /// <summary>
        /// 字符数据缓存工作流池
        /// </summary>
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;

        private IVad? _vad;
        private IAudioDecoder? _audioDecoder;

        
        public AudioReceiveHandler(ObjectPool<Workflow<float[]>> workflowPool,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<AudioReceiveHandler> logger) : base(config, logger)
        {
            this._audioBufferWorkflowPool = workflowPool;
            this._stringWorkflowPool = stringWorkflowPool;
        }
        /// <summary>
        /// 音频数据处理工作流
        /// </summary>
        public event Action<Workflow<string>>? OnNoVoiceCloseConnect;
        /// <summary>
        ///  语音数据处理工作流
        /// </summary>
        public override string HandlerName => nameof(AudioReceiveHandler);
        /// <summary>
        /// 构建 音频数据处理工作流管道 下一步
        /// </summary>
        public ChannelWriter<Workflow<float[]>> NextWriter { get; set; } = null!;
        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Vad is null)
            {
                this.Logger.LogError(Lang.AudioReceiveHandler_Build_VadNotConfigured, session.DeviceId);
                return false;
            }

            if (privateProvider.AudioDecoder is null)
            {
                this.Logger.LogError(Lang.AudioReceiveHandler_Build_AudioDecoderNotConfigured, session.DeviceId);
                return false;
            }

            this._vad = privateProvider.Vad;
            this._vad.RegisterDevice(session.DeviceId, session.SessionId, this);

            this._audioDecoder = privateProvider.AudioDecoder;
            this._audioDecoder.RegisterDevice(session.DeviceId, session.SessionId);

            this.RegisterCancellationToken();
            return true;
        }
      
        public async Task Handle(byte[] opusData)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (this._vad is null)
            {
                this.Logger.LogError(Lang.AudioReceiveHandler_Handle_VadNotConfigured, session.DeviceId);
                return;
            }
            if (this._audioDecoder is null)
            { 
                this.Logger.LogError(Lang.AudioReceiveHandler_Handle_AudioDecoderNotConfigured, session.DeviceId);
                return;
            }
            if (session.IsAudioProcessing && (DateTime.Now - session.LastActivityTime).TotalSeconds > 5)
            {
                session.RejectIncomingAudio();
//#if DEBUG
//                this.Logger.LogDebug(Lang.AudioReceiveHandler_Handle_PacketIgnored);
//#endif
//                return;
            }
            try
            {
                float[] pcmData = await this._audioDecoder.DecodeAsync(opusData, this.HandlerToken);

                this.HandlerToken.ThrowIfCancellationRequested();

                session.AudioPacket.PushAudio(pcmData);

                if (session.ListenMode != ListenMode.Manual)
                {
                    await this._vad.AnalysisVoiceAsync(session.DeviceId, session.SessionId, session.AudioPacket.GetAllAudio(), this.HandlerToken);
                }
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.AudioReceiveHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                session.AudioPacket.Reset();
                this.Logger.LogError(ex, Lang.AudioReceiveHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        /// <summary>
        /// 语音检测事件
        /// </summary>
        /// <param name="audioData"></param>
        public void OnVoiceDetected(float[] audioData)
        {

            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            session.timeoutClose = false;

            session.RejectIncomingAudio();
            session.AudioPacket.ResetAudioBuffer();
            session.AudioPacket.VoiceStop = true;
            this.HandleVoiceDetected(session, audioData);
        }
        /// <summary>
        /// 语音结束事件
        /// </summary>
        public void OnVoiceSilence()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            session.AudioPacket.TrimOldAudio();

        }
        /// <summary>
        /// 长时间无语音事件
        /// </summary>
        public void OnLongTermSilence()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            session.AudioPacket.Reset();

            if (session.timeoutClose)
            {
                return;
            }
            session.timeoutClose = true;  // ← 只在这里设置

            string prompt = "回复限制20个字内，表达依依不舍的再见吧。";

            this.Logger.LogInformation($"设备 {session.SessionId} 10秒无语音输入，准备让LLM说再见");

            var workflow = this._stringWorkflowPool.Get();
            try
            {
                workflow.Initialize(session, prompt);
                this.OnNoVoiceCloseConnect?.Invoke(workflow);
            }
            finally
            {
                this._stringWorkflowPool.Return(workflow);
            }
        }
        /// <summary>
        /// 语音检测事件
        /// </summary>
        /// <param name="session"></param>
        /// <param name="audioData"></param>
        private async void HandleVoiceDetected(Session session, float[] audioData)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            if (audioData.Length < 50)
            {
                // Audio too short, cannot recognize
                this.Logger.LogDebug(Lang.AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort, session.SessionId);
                session.Reset();
                return;
            }
            
            session.RefreshLastActivityTime();
            session.AudioPacket.ResetAudioBuffer();
            var workflow = this._audioBufferWorkflowPool.Get();
            workflow.Initialize(session, audioData);
            
            try
            {
                await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
                // 新增：处理完成后，重新打开门禁
                session.AcceptIncomingAudio();
            }
            catch (OperationCanceledException)
            {
                this._audioBufferWorkflowPool.Return(workflow);

                session.AcceptIncomingAudio(); // 异常时也重置
            }
        }
        /// <summary>
        /// 手动停止
        /// </summary>
        /// <param name="session"></param>
        public void HandleManualStop(Session session)
        {
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            this.HandleVoiceDetected(session, session.AudioPacket.GetAllAudio());
        }
        /// <summary>
        /// 释放
        /// </summary>
        public override void Dispose()
        {

            Session session = this.SendOutter.GetSession();
            if (session is null)
            {
                return;
            }
            this._vad?.UnregisterDevice(session.DeviceId, session.SessionId);
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}

