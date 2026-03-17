using System;

namespace XiaoZhi.Net.Server.Common.Contexts
{
/// <summary>
/// 表示混合音频数据包的类，用于存储音频数据及相关帧信息
/// </summary>
internal class MixedAudioPacket
{
    private float[] _audioData = Array.Empty<float>();
    
    /// <summary>
    /// 初始化 MixedAudioPacket 类的新实例
    /// </summary>
    public MixedAudioPacket()
    {
    }
    
    /// <summary>
    /// 获取音频数据数组
    /// </summary>
    public float[] Data => this._audioData;
    
    /// <summary>
    /// 获取或设置指示是否为第一帧的标志
    /// </summary>
    public bool IsFirstFrame { get; set; }
    
    /// <summary>
    /// 获取或设置指示是否为最后一帧的标志
    /// </summary>
    public bool IsLastFrame { get; set; }
    
    /// <summary>
    /// 获取或设置句子标识符
    /// </summary>
    public string? SentenceId { get; set; }
    
    /// <summary>
    /// 初始化音频数据包的所有属性
    /// </summary>
    /// <param name="audioData">音频数据数组</param>
    /// <param name="isFirstFrame">是否为第一帧</param>
    /// <param name="isLastFrame">是否为最后一帧</param>
    /// <param name="sentenceId">可选的句子标识符</param>
    public void Initialize(float[] audioData, bool isFirstFrame, bool isLastFrame, string? sentenceId = null)
    {
        this._audioData = audioData;
        this.IsFirstFrame = isFirstFrame;
        this.IsLastFrame = isLastFrame;
        this.SentenceId = sentenceId;
    }

    /// <summary>
    /// 重置音频数据包到初始状态，清空所有数据和标志
    /// </summary>
    public void Reset()
    {
        this._audioData = Array.Empty<float>();
        this.IsFirstFrame = false;
        this.IsLastFrame = false;
        this.SentenceId = null;
    }

}
}
