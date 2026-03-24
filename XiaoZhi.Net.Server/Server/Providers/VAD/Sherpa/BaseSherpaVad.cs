using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    /// <summary>
    /// 基础语音活动检测器抽象类，继承自BaseProvider
    /// </summary>
    /// <typeparam name="TLogger">日志记录器类型</typeparam>
    internal abstract class BaseSherpaVad<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        /// <summary>
        /// 语音活动检测器实例
        /// </summary>
        private VoiceActivityDetector? _vad;

        /// <summary>
        /// 采样率，默认为16000Hz
        /// </summary>
        private int _sampleRate = 16000;

        /// <summary>
        /// 无语音时关闭连接的时间阈值，默认120秒
        /// </summary>
        private int _closeConnectionNoVoiceTime = 120;

        /// <summary>
        /// 8K采样率常量
        /// </summary>
        private const int SAMPLING_RATE_8K = 8000;

        /// <summary>
        /// 16K采样率常量
        /// </summary>
        private const int SAMPLING_RATE_16K = 16000;
        /// <summary>
        /// 判断结束静音时长，默认1.0秒
        /// </summary>
        private float _minSilenceDurationSeconds = 1.0f;
        /// <summary>
        /// 用于控制VAD转换的信号量
        /// </summary>
        private readonly SemaphoreSlim _vadConvertSlim;

        /// <summary>
        /// 存储设备ID与语音活动检测会话状态及回调接口的并发字典
        /// </summary>
        private readonly ConcurrentDictionary<string, (VadSessionState, IVadEventCallback)> _vadSessions;

        /// <summary>
        /// 构造函数，初始化语音活动检测器基类
        /// </summary>
        /// <param name="logger">日志记录器实例</param>
        protected BaseSherpaVad(ILogger<TLogger> logger) : base(logger)
        {
            this._vadConvertSlim = new SemaphoreSlim(1, 1);
            this._vadSessions = new ConcurrentDictionary<string, (VadSessionState, IVadEventCallback)>();
        }

        /// <summary>
        /// 获取提供者类型，固定返回"vad"
        /// </summary>
        public override string ProviderType => "vad";

        /// <summary>
        /// 获取帧大小
        /// </summary>
        public int FrameSize { get; private set; }

        /// <summary>
        /// 构建语音活动检测器
        /// </summary>
        /// <param name="vadModelConfig">语音活动检测模型配置</param>
        /// <param name="modelSetting">模型设置</param>
        /// <returns>构建成功返回true，否则返回false</returns>
        public bool Build(VadModelConfig vadModelConfig, ModelSetting modelSetting)
        {
            this._sampleRate = modelSetting.Config.GetConfigValueOrDefault("SampleRate", 16000);

            this._minSilenceDurationSeconds = modelSetting.Config.GetConfigValueOrDefault("SilenceThresholdSecond", 1.0f);

            if (this._sampleRate != SAMPLING_RATE_8K && this._sampleRate != SAMPLING_RATE_16K)
            {
                this.Logger.LogError(Lang.BaseSherpaVad_Build_UnsupportedSampleRate, this._sampleRate);
                return false;
            }

            this._closeConnectionNoVoiceTime = modelSetting.Config.GetConfigValueOrDefault("CloseConnectionNoVoiceTime", 120_000);

            vadModelConfig.SampleRate = this._sampleRate;
            this.FrameSize = this._sampleRate == SAMPLING_RATE_16K ? 512 : 256;
            this._vad = new VoiceActivityDetector(vadModelConfig, 60);

            return true;
        }

        /// <summary>
        /// 注册设备到语音活动检测系统
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="sessionId">会话ID</param>
        /// <param name="callback">语音事件回调接口</param>
        public void RegisterDevice(string deviceId, string sessionId, IVadEventCallback callback)
        {
            VadSessionState vadState = new VadSessionState();
            this._vadSessions.AddOrUpdate(deviceId, (vadState, callback), (_, _) => (vadState, callback));
            this.Logger.LogDebug(Lang.BaseSherpaVad_RegisterDevice_Registered, deviceId, sessionId);
        }

        /// <summary>
        /// 取消注册设备
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="sessionId">会话ID</param>
        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._vadSessions.TryRemove(deviceId, out _))
            {
                this.Logger.LogDebug(Lang.BaseSherpaVad_UnregisterDevice_Unregistered, deviceId, sessionId);
            }
        }

        /// <summary>
        /// 重置会话状态
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="sessionId">会话ID</param>
        public void ResetSessionState(string deviceId, string sessionId)
        {
            if (this._vadSessions.TryGetValue(deviceId, out var context))
            {
                var (state, _) = context;
                state.Reset();
                this.Logger.LogDebug(Lang.BaseSherpaVad_ResetSessionState_Reset, deviceId, sessionId);
            }
        }

        /// <summary>
        /// 检查设备是否已注册
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="sessionId">会话ID</param>
        /// <returns>设备已注册返回true，否则返回false</returns>
        public override bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            return this._vadSessions.ContainsKey(deviceId);
        }

        /// <summary>
        /// 异步分析语音数据
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="sessionId">会话ID</param>
        /// <param name="audioData">音频数据数组</param>
        /// <param name="token">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(deviceId, sessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._vad is null)
            {
                throw new ArgumentNullException(Lang.BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt);
            }

            if (!this._vadSessions.TryGetValue(deviceId, out var context))
            {
                throw new InvalidOperationException(string.Format(Lang.BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound, deviceId, sessionId));
            }
            var (vadState, callback) = context;
            try
            {
                await this._vadConvertSlim.WaitAsync(token);

                this._vad.Reset();

                int analyzedIndex = vadState.AnalyzedIndex;

                // 遍历音频数据的滑动窗口进行语音检测
                while (audioData.GetSlidingFrame(this.FrameSize, ref analyzedIndex, out float[] chunk))
                {
                    token.ThrowIfCancellationRequested();

                    if (chunk.Length == 0)
                    {
                        continue;
                    }

                    this._vad.AcceptWaveform(chunk);

                    bool isSpeaking = this._vad.IsSpeechDetected();

                    if (isSpeaking)
                    {
                        vadState.HaveVoice = true;
                        vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        continue;
                    }
                    else
                    {
                        if (!this._vad.IsEmpty())
                        {
                            SpeechSegment speechSegment = this._vad.Front();
                            this.Logger.LogDebug(Lang.BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped, deviceId);

                            callback.OnVoiceDetected(speechSegment.Samples);
                            vadState.Reset();

                            return;
                        }
                    }
                }

                // 检测长时间静音情况
                if (!this._vad.IsSpeechDetected() && analyzedIndex > this.FrameSize * 50)
                {
                    callback.OnVoiceSilence();
                }

                this.CheckLongTermSilence(deviceId, sessionId, vadState);
            }
            catch (OperationCanceledException)
            {
                vadState.Reset();
                this.Logger.LogWarning(Lang.BaseSherpaVad_AnalysisVoiceAsync_UserCanceled, this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError, this.ProviderType);
            }
            finally
            {
                vadState.AnalyzedIndex = 0;
                this._vad.Reset();
                this._vadConvertSlim.Release();
            }
        }

        /// <summary>
        /// 检查长时间静音状态
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="sessionId">会话ID</param>
        /// <param name="vadState">语音活动检测会话状态</param>
        private void CheckLongTermSilence(string deviceId, string sessionId, VadSessionState vadState)
        {
            if (this._vadSessions.TryGetValue(deviceId, out var context))
            {
                var (_, callback) = context;
                if (vadState.HaveVoiceLatestTime == 0)
                {
                    vadState.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    return;
                }

                long silenceDuration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - vadState.HaveVoiceLatestTime;

                if (silenceDuration >= this._closeConnectionNoVoiceTime)
                {
                    this.Logger.LogDebug(Lang.BaseSherpaVad_CheckLongTermSilence_Detected, deviceId, silenceDuration);
                    callback.OnLongTermSilence();
                }
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            this._vadSessions.Clear();
            this._vadConvertSlim.Dispose();
            this._vad?.Clear();
            this._vad?.Dispose();
        }
    }
}
