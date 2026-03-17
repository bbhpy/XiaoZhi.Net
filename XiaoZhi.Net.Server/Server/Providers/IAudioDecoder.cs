using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 音频解码器 接口
    /// </summary>
    internal interface IAudioDecoder : IProvider<AudioSetting>
    {
        /// <summary>
        /// 采样率
        /// </summary>
        int SampleRate { get; }
        /// <summary>
        /// 声道数
        /// </summary>
        int Channels { get; }
        /// <summary>
        /// 帧时长
        /// </summary>
        int FrameDuration { get; }
        /// <summary>
        ///  帧大小
        /// </summary>
        int FrameSize { get; }
        /// <summary>
        ///  解码 opus数据为pcm数据 
        /// </summary>
        /// <param name="opusData"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<float[]> DecodeAsync(byte[] opusData, CancellationToken token);
    }
}
