namespace XiaoZhi.Net.Server.Media.Abstractions
{
/// <summary>
/// URL音频播放器接口，继承自IAudioPlayer接口
/// 提供从URL加载音频的功能
/// </summary>
public interface IUrlAudioPlayer : IAudioPlayer
{
    /// <summary>
    /// 异步加载音频文件从指定URL
    /// </summary>
    /// <param name="url">音频文件的URL地址</param>
    /// <param name="outputSampleRate">输出采样率</param>
    /// <param name="outputChannels">输出声道数</param>
    /// <param name="frameDuration">帧持续时间</param>
    /// <returns>如果加载成功返回true，否则返回false</returns>
    Task<bool> LoadAsync(string url, int outputSampleRate, int outputChannels, int frameDuration);
}
}
