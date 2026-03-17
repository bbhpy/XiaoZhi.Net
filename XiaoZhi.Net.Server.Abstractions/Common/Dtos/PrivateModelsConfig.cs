namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos
{
/// <summary>
/// 私有模型配置类，用于管理各种AI模型的设置
/// </summary>
public class PrivateModelsConfig
{
    /// <summary>
    /// 语音活动检测(VAD)模型设置
    /// </summary>
    public ModelSetting? VadSetting { get; set; }
    
    /// <summary>
    /// 自动语音识别(ASR)模型设置
    /// </summary>
    public ModelSetting? AsrSetting { get; set; }
    
    /// <summary>
    /// 情感大语言模型设置
    /// </summary>
    public ModelSetting? EmotionLlmSetting { get; set; }
    
    /// <summary>
    /// 聊天大语言模型设置
    /// </summary>
    public ModelSetting? ChatLlmSetting { get; set; }
    
    //public ModelSetting? MemorySetting { get; set; }
    
    /// <summary>
    /// 文本转语音(TTS)模型设置
    /// </summary>
    public ModelSetting? TtsSetting { get; set; }
}
}
