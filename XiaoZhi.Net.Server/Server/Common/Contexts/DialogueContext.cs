using Microsoft.SemanticKernel;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Common.Contexts
{
/// <summary>
/// 表示对话上下文的记录类型，包含会话标识、内核实例、语言模型名称和对话集合
/// </summary>
internal record DialogueContext
{
    /// <summary>
    /// 初始化 <see cref="DialogueContext"/> 类的新实例
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="kernel">Kernel 实例</param>
    /// <param name="llmModelName">语言模型名称，可为空</param>
    /// <param name="dialogues">对话集合</param>
    public DialogueContext(string sessionId, Kernel kernel, string? llmModelName, IEnumerable<Dialogue> dialogues)
    {
        this.SessionId = sessionId;
        this.Kernel = kernel;
        this.LlmModelName = llmModelName;
        this.Dialogues = dialogues;
    }

    /// <summary>
    /// 获取会话标识符
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// 获取 Kernel 实例
    /// </summary>
    public Kernel Kernel { get; }

    /// <summary>
    /// 获取语言模型名称，可能为空
    /// </summary>
    public string? LlmModelName { get; }

    /// <summary>
    /// 获取对话集合
    /// </summary>
    public IEnumerable<Dialogue> Dialogues { get; }
}
}
