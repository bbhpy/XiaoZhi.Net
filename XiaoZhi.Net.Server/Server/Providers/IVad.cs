using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Providers.VAD;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 语音检测 接口
    /// </summary>
    internal interface IVad : IProvider<ModelSetting>
    {
        /// <summary>
        /// 帧大小
        /// </summary>
        int FrameSize { get; }
        /// <summary>
        /// 注册设备
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sessionId"></param>
        /// <param name="callback"></param>
        void RegisterDevice(string deviceId, string sessionId, IVadEventCallback callback);
        /// <summary>
        /// 分析语音
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sessionId"></param>
        /// <param name="audioData"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token);
        /// <summary>
        /// 重置会话状态
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sessionId"></param>
        void ResetSessionState(string deviceId, string sessionId);
    }
}
