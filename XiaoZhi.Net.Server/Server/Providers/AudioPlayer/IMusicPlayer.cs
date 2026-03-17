using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
/// <summary>
/// 音乐播放器接口，继承自音频设置提供者接口
/// </summary>
internal interface IMusicPlayer : IProvider<AudioSetting>
{
    /// <summary>
    /// 音频数据事件，在有新的音频数据时触发
    /// </summary>
    /// <param name="float[]">音频采样数据数组</param>
    /// <param name="bool">是否为立体声</param>
    /// <param name="bool">是否为新数据</param>
    event Action<float[], bool, bool> OnAudioData;

    /// <summary>
    /// 获取当前正在播放的音乐名称，可能为空
    /// </summary>
    string? PlayingMusicName { get; }

    /// <summary>
    /// 获取播放状态
    /// </summary>
    PlaybackState PlaybackState { get; }

    /// <summary>
    /// 获取是否正在播放
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// 获取或设置音量
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// 异步播放音乐
    /// </summary>
    /// <param name="cancellationToken">取消令牌，默认为默认值</param>
    /// <param name="sources">音乐源文件路径数组</param>
    /// <returns>异步任务</returns>
    Task PlayAsync(CancellationToken cancellationToken = default, params string[] sources);

    /// <summary>
    /// 异步暂停播放
    /// </summary>
    /// <returns>异步任务</returns>
    Task PauseAsync();

    /// <summary>
    /// 异步恢复播放
    /// </summary>
    /// <returns>异步任务</returns>
    Task ResumeAsync();

    /// <summary>
    /// 异步跳转到指定位置
    /// </summary>
    /// <param name="position">要跳转到的时间位置</param>
    /// <returns>异步任务</returns>
    Task SeekAsync(TimeSpan position);

    /// <summary>
    /// 异步停止播放
    /// </summary>
    /// <returns>异步任务</returns>
    Task StopAsync();
}
}
