using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
/// <summary>
/// 音频输出片段类，继承自OutSegment，用于处理音频数据的输出片段
/// </summary>
internal class OutAudioSegment : OutSegment
{
    /// <summary>
    /// 存储音频数据的浮点数组，默认为空数组
    /// </summary>
    private float[] _audioData = Array.Empty<float>();

    /// <summary>
    /// 构造函数，初始化OutAudioSegment实例
    /// </summary>
    public OutAudioSegment() : base()
    {
    }

    /// <summary>
    /// 获取当前音频片段的音频数据
    /// </summary>
    public float[] AudioData => this._audioData;

    /// <summary>
    /// 获取或设置音频类型
    /// </summary>
    public AudioType AudioType { get; private set; }
    
    /// <summary>
    /// 获取或设置是否为第一帧
    /// </summary>
    public bool IsFirstFrame { get; set; }
    
    /// <summary>
    /// 获取或设置是否为最后一帧
    /// </summary>
    public bool IsLastFrame { get; set; }

    /// <summary>
    /// 初始化音频输出片段的各种属性
    /// </summary>
    /// <param name="audioData">音频数据数组，可为null</param>
    /// <param name="audioType">音频类型，默认为None</param>
    /// <param name="content">内容字符串，可为null</param>
    /// <param name="isFirstSegment">是否为第一个片段，默认为false</param>
    /// <param name="isLastSegment">是否为最后一个片段，默认为false</param>
    /// <param name="isFirstFrame">是否为第一帧，默认为false</param>
    /// <param name="isLastFrame">是否为最后一帧，默认为false</param>
    /// <param name="emotion">情感类型，默认为Neutral</param>
    /// <param name="sentenceId">句子ID，可为null</param>
    public void Initialize(float[]? audioData = null, AudioType audioType = AudioType.None, string? content = null, bool isFirstSegment = false, bool isLastSegment = false,
        bool isFirstFrame = false, bool isLastFrame = false, Emotion emotion = Emotion.Neutral, string? sentenceId = null)
    {
        this._audioData = audioData ?? Array.Empty<float>();
        this.AudioType = audioType;
        this.IsFirstFrame = isFirstFrame;
        this.IsLastFrame = isLastFrame;
        base.Initialize(content ?? string.Empty, isFirstSegment, isLastSegment, emotion, sentenceId: sentenceId);
    }

    /// <summary>
    /// 重置音频输出片段的所有属性到默认状态
    /// </summary>
    public override void Reset()
    {
        this._audioData = Array.Empty<float>();
        this.AudioType = AudioType.None;
        this.IsFirstFrame = false;
        this.IsLastFrame = false;
        base.Reset();
    }
}
}
