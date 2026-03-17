using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
/// <summary>
/// 音频编码器接口，提供音频编码功能和相关设置
/// </summary>
internal interface IAudioEncoder : IProvider<AudioSetting>
{
    /// <summary>
    /// 获取音频采样率（Hz）
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// 获取音频声道数
    /// </summary>
    int Channels { get; }

    /// <summary>
    /// 获取音频帧时长（毫秒）
    /// </summary>
    int FrameDuration { get; }

    /// <summary>
    /// 获取音频帧大小（字节数）
    /// </summary>
    int FrameSize { get; }

    /// <summary>
    /// 异步编码PCM数据为音频格式
    /// </summary>
    /// <param name="pcmData">输入的PCM浮点数组数据</param>
    /// <param name="token">取消操作的令牌</param>
    /// <returns>编码后的字节数组</returns>
    Task<byte[]> EncodeAsync(float[] pcmData, CancellationToken token);
}
}
