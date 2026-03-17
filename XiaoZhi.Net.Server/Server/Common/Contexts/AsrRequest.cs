using SherpaOnnx;
using System.Threading;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Common.Contexts
{
/// <summary>
/// ASR(自动语音识别)请求类，用于封装语音识别请求的相关参数和配置信息
/// </summary>
internal class AsrRequest
{
    /// <summary>
    /// 初始化ASR请求实例
    /// </summary>
    /// <param name="sessionId">会话ID，用于标识当前语音识别会话</param>
    /// <param name="deviceId">设备ID，用于标识发起请求的设备</param>
    /// <param name="stream">离线音频流，包含待识别的音频数据</param>
    /// <param name="sampleRate">采样率，指定音频数据的采样频率</param>
    /// <param name="frameSize">帧大小，指定每次处理的音频帧大小</param>
    /// <param name="callback">ASR事件回调接口，用于接收识别结果和状态更新</param>
    /// <param name="token">取消令牌，用于控制请求的取消操作</param>
    public AsrRequest(string sessionId, string deviceId, OfflineStream stream, int sampleRate, int frameSize, IAsrEventCallback callback,  CancellationToken token)
    {
        this.SessionId = sessionId;
        this.DeviceId = deviceId;
        this.Stream = stream;
        this.SampleRate = sampleRate;
        this.FrameSize = frameSize;
        this.Callback = callback;
        this.Token = token;
    }

    /// <summary>
    /// 获取或设置会话ID
    /// </summary>
    public string SessionId { get; set; }
    
    /// <summary>
    /// 获取或设置设备ID
    /// </summary>
    public string DeviceId { get; set; }
    
    /// <summary>
    /// 获取或设置离线音频流
    /// </summary>
    public OfflineStream Stream { get; set; }
    
    /// <summary>
    /// 获取或设置采样率
    /// </summary>
    public int SampleRate { get; set; }
    
    /// <summary>
    /// 获取或设置帧大小
    /// </summary>
    public int FrameSize { get; set; }
    
    /// <summary>
    /// 获取或设置ASR事件回调接口
    /// </summary>
    public IAsrEventCallback Callback { get; set; }
    
    /// <summary>
    /// 获取或设置取消令牌
    /// </summary>
    public CancellationToken Token { get; set; }
}
}
