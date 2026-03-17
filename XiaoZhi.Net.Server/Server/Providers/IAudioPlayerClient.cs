using XiaoZhi.Net.Server.Providers.AudioPlayer;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 音频播放客户端
    /// </summary>
    internal interface IAudioPlayerClient : IProvider<AudioSetting>
    {
        /// <summary>
        /// 音乐播放器
        /// </summary>
        IMusicPlayer MusicPlayer { get; }
        /// <summary>
        ///  系统提示
        /// </summary>
        ISystemNotification SystemNotification { get; }
    }
}
