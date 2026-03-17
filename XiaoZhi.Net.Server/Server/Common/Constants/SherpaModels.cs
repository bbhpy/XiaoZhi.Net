using XiaoZhi.Net.Server.Providers.ASR.Sherpa;
using XiaoZhi.Net.Server.Providers.TTS.Sherpa;
using XiaoZhi.Net.Server.Providers.VAD.Sherpa;

namespace XiaoZhi.Net.Server.Common.Constants
{
/// <summary>
/// 提供语音处理相关模型配置的静态类，包含VAD（语音活动检测）、ASR（自动语音识别）和TTS（文本转语音）模型列表
/// </summary>
internal static class SherpaModels
{
    /// <summary>
    /// 语音活动检测（VAD）模型名称数组
    /// 包含当前支持的VAD模型：Silero
    /// </summary>
    public static readonly string[] VadModels = [nameof(Silero)];

    /// <summary>
    /// 自动语音识别（ASR）模型名称数组
    /// 包含当前支持的ASR模型：SenseVoice、Paraformer
    /// </summary>
    public static readonly string[] AsrModels = [nameof(SenseVoice), nameof(Paraformer)];

    /// <summary>
    /// 文本转语音（TTS）模型名称数组
    /// 包含当前支持的TTS模型：Kokoro
    /// </summary>
    public static readonly string[] TtsModels = [nameof(Kokoro)];
}
}
