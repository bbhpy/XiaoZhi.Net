using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
/// <summary>
/// 音频字幕注册器接口，用于管理音频和字幕的注册、查询和清理操作
/// </summary>
public interface IAudioSubtitleRegister : IDisposable
{
    /// <summary>
    /// 注册音频字幕信息
    /// </summary>
    /// <param name="sentenceId">句子标识符，用于唯一标识一个句子</param>
    /// <param name="audioType">音频类型</param>
    /// <param name="ttsStatus">TTS（文本转语音）状态</param>
    /// <param name="subtitleText">字幕文本内容</param>
    /// <param name="emotion">情感类型</param>
    void Register(string sentenceId, AudioType audioType, TtsStatus ttsStatus, string subtitleText, Emotion emotion);

    /// <summary>
    /// 根据句子ID获取对应的音频字幕信息
    /// </summary>
    /// <param name="sentenceId">句子标识符</param>
    /// <param name="subtitle">输出参数，返回查找到的音频字幕对象</param>
    /// <returns>如果找到对应的字幕信息则返回true，否则返回false</returns>
    bool GetSubtitle(string sentenceId, out AudioSubtitle subtitle);

    /// <summary>
    /// 清空所有已注册的音频字幕信息
    /// </summary>
    void ClearAll();
}

}
