using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
  /// <summary>
/// 音频混音器接口，定义了音频混合功能的基本操作和事件
/// </summary>
public interface IAudioMixer : IDisposable
{
    /// <summary>
    /// 混音器状态变化事件
    /// 当混音器内部状态发生改变时触发
    /// </summary>
    event Action<AudioMixerState> OnStateChanged;

    /// <summary>
    /// 混合音频数据可用事件
    /// 当混音器完成音频数据混合后触发，提供混合后的音频数据
    /// </summary>
    event Action<float[], bool, bool, string?>? OnMixedAudioDataAvailable;

    /// <summary>
    /// 音频统计信息更新事件
    /// 当混音统计数据更新时触发
    /// </summary>
    event Action<AudioMixerStats> OnMixingStatsUpdated;

    /// <summary>
    /// 获取混音器是否已初始化的状态
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 输出音频采样率（赫兹）
    /// 表示混音器输出音频的采样频率
    /// </summary>
    int OutputSampleRate { get; }

    /// <summary>
    /// 输出音频通道数
    /// 表示混音器输出音频的声道数量
    /// </summary>
    int OutputChannels { get; }

    /// <summary>
    /// 帧持续时间（毫秒）
    /// 表示每个音频帧的时间长度
    /// </summary>
    int FrameDuration { get; }

    /// <summary>
    /// 初始化音频混音器
    /// </summary>
    /// <param name="outputSampleRate">输出采样率</param>
    /// <param name="outputChannels">输出通道数</param>
    /// <param name="frameDuration">帧持续时间（毫秒）</param>
    /// <param name="config">混音器配置，可选参数</param>
    /// <returns>初始化是否成功</returns>
    bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null);

    /// <summary>
    /// 向指定优先级的音频混音器添加音频数据
    /// </summary>
    /// <param name="audioType">带优先级的音频类型</param>
    /// <param name="audioData">音频数据数组</param>
    /// <param name="sentenceId">用于字幕同步的可选句子ID</param>
    void AddAudioData(AudioType audioType, float[] audioData, string? sentenceId = null);

    /// <summary>
    /// 停止指定类型的音频流
    /// </summary>
    /// <param name="audioType">要停止的音频类型</param>
    void StopAudioStream(AudioType audioType);

    /// <summary>
    /// 清空所有音频缓冲区
    /// </summary>
    void ClearAllBuffers();

    /// <summary>
    /// 获取当前混音器统计信息
    /// </summary>
    /// <returns>当前混音器统计信息</returns>
    AudioMixerStats GetCurrentStats();
}
}
