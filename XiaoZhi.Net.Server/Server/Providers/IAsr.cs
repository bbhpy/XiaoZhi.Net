using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Providers
{
/// <summary>
/// 语音识别服务接口，继承自IProvider<ModelSetting>，提供语音转文字的相关功能
/// </summary>
internal interface IAsr : IProvider<ModelSetting>
{
    /// <summary>
    /// 注册设备到语音识别服务
    /// </summary>
    /// <param name="deviceId">设备唯一标识符</param>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="callback">语音识别事件回调接口</param>
    void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback);

    /// <summary>
    /// 异步将语音数据转换为文本
    /// </summary>
    /// <param name="workflow">包含音频数据的工作流，音频数据以float数组形式存储</param>
    /// <param name="sampleRate">音频采样率</param>
    /// <param name="frameSize">音频帧大小</param>
    /// <param name="token">取消操作的令牌</param>
    /// <returns>异步任务，用于等待语音转文字操作完成</returns>
    Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token);
}
}
