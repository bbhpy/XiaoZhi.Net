using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Server.Common.Configs
{
/// <summary>
/// LLM构建配置记录类，用于存储和传递大语言模型的配置信息
/// </summary>
/// <param name="EmotionLLMModelName">情感分析LLM模型名称</param>
/// <param name="ChatLLMModelName">聊天LLM模型名称</param>
/// <param name="Prompt">提示词模板</param>
/// <param name="UseStreaming">是否使用流式传输</param>
/// <param name="UseEmotions">是否使用情感功能</param>
/// <param name="SummaryMemory">摘要记忆类型或配置</param>
/// <param name="Kernel">Kernel实例</param>
internal record LLMBuildConfig(
        string EmotionLLMModelName,
        string ChatLLMModelName,
        string Prompt,
        bool UseStreaming,
        bool UseEmotions,
        string SummaryMemory,
        Kernel Kernel);
}
