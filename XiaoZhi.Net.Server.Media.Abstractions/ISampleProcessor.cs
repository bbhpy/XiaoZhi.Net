namespace XiaoZhi.Net.Server.Media.Abstractions;

/// <summary>
/// 音频样本处理器接口，用于在音频样本发送到输出设备之前对其进行操作
/// 该接口专门处理 Float32 格式的音频样本数据
/// </summary>
public interface ISampleProcessor
{
    /// <summary>
    /// 获取或设置样本处理器的启用状态
    /// 此属性供外部使用，不应影响 Process 方法的执行逻辑
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 处理或操作给定的 Float32 格式音频样本
    /// 对输入的音频样本应用特定的音频处理算法或效果
    /// </summary>
    /// <param name="sample">待处理的音频样本，格式为 Float32</param>
    /// <returns>处理后的音频样本，格式为 Float32</returns>
    float Process(float sample);
}
