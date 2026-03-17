using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.TTS;

namespace XiaoZhi.Net.Server.Providers
{
/// <summary>
/// 文本转语音服务接口，继承自IProvider<ModelSetting>接口
/// 提供文本转语音的核心功能，包括采样率获取、设备注册和语音合成等操作
/// </summary>
internal interface ITts : IProvider<ModelSetting>
{
    /// <summary>
    /// 获取TTS语音合成的采样率
    /// </summary>
    /// <returns>返回音频采样率，单位为Hz</returns>
    int GetTtsSampleRate();

    /// <summary>
    /// 注册TTS设备，将设备与会话和回调函数关联
    /// </summary>
    /// <param name="deviceId">设备唯一标识符</param>
    /// <param name="sessionId">会话唯一标识符</param>
    /// <param name="callback">TTS事件回调接口，用于处理语音合成过程中的各种事件</param>
    void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback);

    /// <summary>
    /// 异步执行语音合成操作
    /// </summary>
    /// <param name="workflow">包含输出片段的工作流对象，定义了语音合成的具体流程</param>
    /// <param name="token">取消令牌，用于控制异步操作的取消</param>
    /// <returns>表示异步操作的任务对象</returns>
    Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token);
}
}
