namespace XiaoZhi.Net.Server.Media.Abstractions
{
    /// <summary>
/// 音频编辑器接口，定义了保存音频文件的各种方法
/// 提供异步保存浮点音频数据和PCM数据到文件的功能，支持默认设置和自定义设置
/// </summary>
public interface IAudioEditor
{
    /// <summary>
    /// 使用默认设置（16kHz，单声道，128kbps）将浮点音频数据保存到文件
    /// </summary>
    /// <param name="filePath">输出文件路径（格式由扩展名决定）</param>
    /// <param name="data">音频样本，归一化到[-1.0, 1.0]范围</param>
    /// <returns>如果保存成功返回true，否则返回false</returns>
    Task<bool> SaveAudioFileAsync(string filePath, float[] data);

    /// <summary>
    /// 使用指定设置将浮点音频数据保存到文件
    /// </summary>
    /// <param name="filePath">输出文件路径（格式由扩展名决定）</param>
    /// <param name="data">音频样本，归一化到[-1.0, 1.0]范围</param>
    /// <param name="sampleRate">采样率，单位Hz</param>
    /// <param name="channels">音频通道数</param>
    /// <param name="bitRate">编码比特率</param>
    /// <returns>如果保存成功返回true，否则返回false</returns>
    Task<bool> SaveAudioFileAsync(string filePath, float[] data, int sampleRate, int channels, int bitRate);

    /// <summary>
    /// 使用默认设置（16kHz，单声道，128kbps）将16位有符号小端序PCM数据保存到文件
    /// </summary>
    /// <param name="filePath">输出文件路径（格式由扩展名决定）</param>
    /// <param name="pcmData">原始16位有符号小端序PCM字节数组</param>
    /// <returns>如果保存成功返回true，否则返回false</returns>
    Task<bool> SaveAudioFileAsync(string filePath, byte[] pcmData);

    /// <summary>
    /// 使用指定设置将16位有符号小端序PCM数据保存到文件
    /// </summary>
    /// <param name="filePath">输出文件路径（格式由扩展名决定）</param>
    /// <param name="pcmData">原始16位有符号小端序PCM字节数组</param>
    /// <param name="sampleRate">采样率，单位Hz</param>
    /// <param name="channels">音频通道数</param>
    /// <param name="bitRate">编码比特率</param>
    /// <returns>如果保存成功返回true，否则返回false</returns>
    Task<bool> SaveAudioFileAsync(string filePath, byte[] pcmData, int sampleRate, int channels, int bitRate);
}
}
