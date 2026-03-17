namespace XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

/// <summary>
/// 表示媒体播放器的播放状态枚举
/// 定义了播放器可能处于的不同状态
/// </summary>
public enum PlaybackState
{
    /// <summary>
    /// 空闲状态 - 播放器当前没有进行任何播放操作
    /// </summary>
    Idle,

    /// <summary>
    /// 播放状态 - 媒体正在正常播放中
    /// </summary>
    Playing,

    /// <summary>
    /// 缓冲状态 - 播放器正在缓冲数据以准备播放
    /// </summary>
    Buffering,

    /// <summary>
    /// 暂停状态 - 播放已暂停，可以从中断点继续播放
    /// </summary>
    Paused
}
