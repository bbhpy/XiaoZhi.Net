using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions.Dtos
{
/// <summary>
/// 表示音频字幕数据结构，包含句子标识、音频类型、字幕文本、情感、TTS状态和注册时间信息
/// </summary>
public struct AudioSubtitle
{
    /// <summary>
    /// 初始化AudioSubtitle实例的新对象
    /// </summary>
    /// <param name="sentenceId">句子标识符</param>
    /// <param name="audioType">音频类型</param>
    /// <param name="subtitleText">字幕文本内容</param>
    /// <param name="emotion">情感类型</param>
    /// <param name="ttsStatus">TTS（文本转语音）状态</param>
    /// <param name="registerTime">注册时间</param>
    public AudioSubtitle(string sentenceId, AudioType audioType, string subtitleText, Emotion emotion, TtsStatus ttsStatus, DateTime registerTime)
    {
        this.SentenceId = sentenceId;
        this.AudioType = audioType;
        this.SubtitleText = subtitleText;
        this.Emotion = emotion;
        this.TtsStatus = ttsStatus;
        this.RegisterTime = registerTime;
    }

    /// <summary>
    /// 获取句子标识符
    /// </summary>
    public string SentenceId { get; } = string.Empty;

    /// <summary>
    /// 获取音频类型
    /// </summary>
    public AudioType AudioType { get; }

    /// <summary>
    /// 获取字幕文本内容
    /// </summary>
    public string SubtitleText { get; } = string.Empty;

    /// <summary>
    /// 获取情感类型
    /// </summary>
    public Emotion Emotion { get; }

    /// <summary>
    /// 获取注册时间
    /// </summary>
    public DateTime RegisterTime { get; }

    /// <summary>
    /// 获取TTS（文本转语音）状态
    /// </summary>
    public TtsStatus TtsStatus { get; }
}
}


