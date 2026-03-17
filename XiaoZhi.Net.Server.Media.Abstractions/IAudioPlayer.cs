using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions;

/// <summary>
/// 音频播放器接口，定义了音频加载和播放控制的基本功能
/// <para>实现: <see cref="IDisposable"/>.</para>
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>
    /// 当播放状态发生改变时触发的事件
    /// </summary>
    event Action<PlaybackState> StateChanged;

    /// <summary>
    /// 当播放位置发生改变时触发的事件
    /// </summary>
    event Action<TimeSpan> PositionChanged;

    /// <summary>
    /// 当音频数据可用时触发的事件
    /// </summary>
    /// <param name="audioData">音频数据样本</param>
    /// <param name="isFirst">指示这是否是第一个音频帧</param>
    /// <param name="isLast">指示这是否是最后一个音频帧</param>
    event Action<float[], bool, bool> OnAudioDataAvailable;

    /// <summary>
    /// 获取一个值，指示FFmpeg是否已成功初始化
    /// </summary>
    public bool IsFFmpegInitialized { get; }

    /// <summary>
    /// 获取音频源是否已加载并准备好播放
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// 获取已加载音频文件的总时长
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// 获取当前播放器位置
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// 获取播放器当前是否正在搜索音频流
    /// </summary>
    bool IsSeeking { get; }

    /// <summary>
    /// 获取或设置音频音量
    /// 范围：0 ~ 1.0f，默认为最大值
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// 获取或设置自定义样本处理器
    /// </summary>
    ISampleProcessor? CustomSampleProcessor { get; set; }

    /// <summary>
    /// 检查FFmpeg是否已安装并初始化以供使用
    /// </summary>
    /// <remarks>
    /// 此方法验证FFmpeg的初始化状态。如果FFmpeg未初始化，
    /// 它将尝试初始化它。如果在初始化过程中发生错误，该方法将记录错误并返回<see langword="false"/>。
    /// </remarks>
    /// <returns>如果FFmpeg成功初始化则返回<see langword="true"/>；否则返回<see langword="false"/></returns>
    bool CheckFFmpegInstalled();

    /// <summary>
    /// 播放与此实例关联的音频或媒体
    /// </summary>
    /// <param name="waitDone">指示方法是否应在播放完成前阻塞执行。<see langword="true"/>等待播放完成；否则为<see langword="false"/></param>
    void Play(bool waitDone = false);

    /// <summary>
    /// 暂停播放器向输出设备发送缓冲区
    /// </summary>
    void Pause();

    /// <summary>
    /// 停止播放
    /// </summary>
    void Stop();

    /// <summary>
    /// 将已加载的音频搜索到指定位置
    /// </summary>
    /// <param name="position">期望的搜索位置</param>
    void Seek(TimeSpan position);
}