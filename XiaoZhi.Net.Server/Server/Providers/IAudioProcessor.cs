using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 音频处理器接口，继承自IProvider<AudioSetting>，用于处理音频数据的混合、处理和字幕管理
    /// </summary>
    internal interface IAudioProcessor : IProvider<AudioSetting>
    {
        /// <summary>
        /// 混合音频数据可用时触发的事件
        /// </summary>
        /// <param name="audioData">音频数据数组</param>
        /// <param name="isFirstFrame">是否为第一帧</param>
        /// <param name="isLastFrame">是否为最后一帧</param>
        /// <param name="sentenceId">句子ID，可为空</param>
        event Action<float[], bool, bool, string?> OnMixedAudioDataAvailable;

        /// <summary>
        /// 处理音频数据
        /// </summary>
        /// <param name="audioType">音频类型</param>
        /// <param name="audioData">音频数据数组</param>
        /// <param name="content">音频内容</param>
        /// <param name="emotion">情感类型</param>
        /// <param name="isFirstFrame">是否为第一帧</param>
        /// <param name="isLastFrame">是否为最后一帧</param>
        /// <param name="sentenceId">句子ID</param>
        void ProcessAudio(AudioType audioType, float[] audioData, string content, Emotion emotion, bool isFirstFrame, bool isLastFrame, string? sentenceId);
        /// <summary>
        /// 完成音频流处理
        /// </summary>
        /// <param name="audioType">音频类型</param>
        void CompleteStream(AudioType audioType);
        /// <summary>
        /// 清空所有缓冲区
        /// </summary>
        void ClearAllBuffers();
        /// <summary>
        /// 注册字幕信息
        /// </summary>
        /// <param name="sentenceId">句子ID</param>
        /// <param name="audioType">音频类型</param>
        /// <param name="ttsStatus">TTS状态</param>
        /// <param name="text">文本内容</param>
        /// <param name="emotion">情感类型</param>
        void RegisterSubtitle(string sentenceId, AudioType audioType, TtsStatus ttsStatus, string text, Emotion emotion);
        /// <summary>
        /// 获取指定句子ID的字幕信息
        /// </summary>
        /// <param name="sentenceId">句子ID</param>
        /// <param name="subtitle">输出的音频字幕对象</param>
        /// <returns>如果找到对应的字幕信息则返回true，否则返回false</returns>
        bool GetSubtitle(string sentenceId, out AudioSubtitle subtitle);
    }

}
