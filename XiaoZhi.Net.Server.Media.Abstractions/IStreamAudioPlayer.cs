namespace XiaoZhi.Net.Server.Media.Abstractions
{
    /// <summary>
    /// 音频流播放器接口
    /// </summary>
public interface IStreamAudioPlayer : IAudioPlayer
{
    /// <summary>
    /// 将音频流加载到播放器中
    /// </summary>
    /// <param name="stream">源音频流</param>
    /// <param name="outputSampleRate">期望的输出采样率</param>
    /// <param name="outputChannels">期望的输出声道数</param>
    /// <param name="frameDuration">期望的输出帧持续时间（毫秒）</param>
    /// <returns>如果加载成功返回<c>true</c>，否则返回<c>false</c></returns>
    Task<bool> LoadAsync(Stream stream, int outputSampleRate, int outputChannels, int frameDuration);
}
}
