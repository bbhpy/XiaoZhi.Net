using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Editors;
using XiaoZhi.Net.Server.Media.Encoders;
using XiaoZhi.Net.Server.Media.Encoders.FFmpeg;
using XiaoZhi.Net.Server.Media.Mixers;
using XiaoZhi.Net.Server.Media.Players;
using XiaoZhi.Net.Server.Media.Subtitle;
using XiaoZhi.Net.Server.Media.Utilities;

namespace XiaoZhi.Net.Server.Media
{
    /// <summary>
    /// 媒体工厂类，用于创建各种媒体相关组件，包括音频播放器、混音器和编辑器
    /// 提供初始化FFmpeg和创建不同媒体处理组件实例的方法
    /// </summary>
    public static class MediaFactory
    {
        /// <summary>
        /// 从指定路径注册FFmpeg二进制文件
        /// </summary>
        /// <param name="ffmpegPath">FFmpeg二进制文件目录的路径，默认为"./ffmpeg/"</param>
        public static void InitializeFFmpeg(string ffmpegPath = "./ffmpeg/")
        {
            FFmpegStartup.RegisterFFmpegBinaries(ffmpegPath);
        }

        /// <summary>
        /// 检查系统上是否安装了FFmpeg并获取已安装的版本
        /// </summary>
        /// <param name="ffmpegVersion">当此方法返回时，包含系统上安装的FFmpeg版本，如果未安装则为字符串</param>
        /// <returns>如果安装了FFmpeg，则为<see langword="true"/>；否则为<see langword="false"/></returns>
        public static bool CheckFFmpegInstalled(out string ffmpegVersion)
        {
            return FFmpegStartup.CheckFFmpegInstalled(out ffmpegVersion);
        }

        /// <summary>
        /// 创建一个从URL播放音频的新音频播放器实例
        /// </summary>
        /// <returns>配置为从URL源播放音频的<see cref="IUrlAudioPlayer"/>实例</returns>
        public static IUrlAudioPlayer CreateUrlAudioPlayer()
        {
            return new UrlAudioPlayer(NullLoggerFactory.Instance.CreateLogger<UrlAudioPlayer>());
        }

        /// <summary>
        /// 创建一个专为流式音频设计的新音频播放器实例
        /// </summary>
        /// <remarks>返回的音频播放器已使用默认日志记录实例初始化。
        /// 适用于需要流式传输和实时播放音频的场景。</remarks>
        /// <returns>配置为流式音频播放的<see cref="IStreamAudioPlayer"/>实例</returns>
        public static IStreamAudioPlayer CreateStreamAudioPlayer()
        {
            return new StreamAudioPlayer(NullLoggerFactory.Instance.CreateLogger<StreamAudioPlayer>());
        }

        /// <summary>
        /// 创建并返回一个新的音视频字幕同步跟踪器实例
        /// </summary>
        /// <returns>用于跟踪和管理音视频和字幕流之间同步的<see cref="IAudioSubtitleRegister"/>实例</returns>
        public static IAudioSubtitleRegister CreateAudioSubtitleSyncTracker()
        {
            return new AudioSubtitleRegister(NullLoggerFactory.Instance.CreateLogger<AudioSubtitleRegister>());
        }

        /// <summary>
        /// 创建一个用于实时多流音频混音的新音频混音器实例
        /// </summary>
        /// <param name="sampleRate">输出采样率（Hz）</param>
        /// <param name="channels">输出声道数</param>
        /// <param name="frameDuration">帧持续时间（毫秒）</param>
        /// <param name="config">音频混音器的可选配置</param>
        /// <returns>配置为多流音频混音的<see cref="IAudioMixer"/>实例</returns>
        public static IAudioMixer CreateAudioMixer(int sampleRate, int channels, int frameDuration, AudioMixerConfig? config = null)
        {
            IAudioMixer mixer = new AudioMixer(NullLoggerFactory.Instance.CreateLogger<AudioMixer>());

            if (!mixer.Initialize(sampleRate, channels, frameDuration, config))
            {
                mixer.Dispose();
                throw new InvalidOperationException($"Failed to initialize FFmpegAudioMixer with parameters: sampleRate={sampleRate}, channels={channels}, frameDuration={frameDuration}");
            }

            return mixer;
        }

        /// <summary>
        /// 创建一个用于实时多流音频混音的新音频混音器实例
        /// </summary>
        /// <param name="sampleRate">输出采样率（Hz）</param>
        /// <param name="channels">输出声道数</param>
        /// <param name="frameDuration">帧持续时间（毫秒）</param>
        /// <param name="config">音频混音器的可选配置</param>
        /// <returns>配置为多流音频混音的<see cref="IAudioMixer"/>实例</returns>
        public static IAudioMixer CreateFFmpegAudioMixer(int sampleRate, int channels, int frameDuration, AudioMixerConfig? config = null)
        {
            IAudioMixer mixer = new FFmpegAudioMixer(NullLoggerFactory.Instance.CreateLogger<FFmpegAudioMixer>());

            if (!mixer.Initialize(sampleRate, channels, frameDuration, config))
            {
                mixer.Dispose();
                throw new InvalidOperationException($"Failed to initialize FFmpegAudioMixer with parameters: sampleRate={sampleRate}, channels={channels}, frameDuration={frameDuration}");
            }

            return mixer;
        }

        /// <summary>
        /// 使用基于FFmpeg的音频编码器创建一个新的IAudioEditor实例
        /// </summary>
        /// <returns>使用FFmpegEncoder初始化的IAudioEditor</returns>
        public static IAudioEditor CreateAudioEditor()
        {
            IAudioEncoder audioEncoder = new FFmpegEncoder(NullLoggerFactory.Instance.CreateLogger<FFmpegEncoder>());

            return new AudioEditor(audioEncoder);
        }
    }
}
