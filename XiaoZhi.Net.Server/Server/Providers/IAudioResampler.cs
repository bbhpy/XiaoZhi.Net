using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
/// <summary>
/// 音频重采样器接口，用于处理音频数据的采样率转换
/// </summary>
internal interface IAudioResampler : IProvider<ResamplerBuildConfig>
{
    /// <summary>
    /// 获取音频通道数
    /// </summary>
    public int Channels { get; }
    
    /// <summary>
    /// 获取输入音频的采样率
    /// </summary>
    public int InSampleRate { get; }
    
    /// <summary>
    /// 获取输出音频的采样率
    /// </summary>
    public int OutSampleRate { get; }
    
    /// <summary>
    /// 异步执行音频重采样操作
    /// </summary>
    /// <param name="inputData">输入的音频数据数组</param>
    /// <param name="token">取消令牌，用于控制异步操作的取消</param>
    /// <returns>返回一个元组，包含重采样后的音频数据数组和实际处理的样本数量</returns>
    Task<(float[], int)> ResampleAsync(float[] inputData, CancellationToken token);
}
}
