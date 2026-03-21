using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
/// <summary>
/// 大语言模型接口，继承自IProvider<LLMBuildConfig>
/// 定义了大语言模型的基本操作和事件回调机制
/// </summary>
internal interface ILlm : IProvider<LLMBuildConfig>
{
    /// <summary>
    /// 在生成令牌之前的事件
    /// </summary>
    event Action OnBeforeTokenGenerate;

    /// <summary>
    /// 在令牌生成过程中的事件
    /// </summary>
    event Action<OutSegment> OnTokenGenerating;

    /// <summary>
    /// 在令牌生成完成后的事件
    /// </summary>
    /// <param name="segments">输出段集合</param>
    event Action<IEnumerable<OutSegment>> OnTokenGenerated;

    /// <summary>
    /// 获取是否使用流式传输
    /// </summary>
    bool UseStreaming { get; }

    /// <summary>
    /// 获取大语言模型对话历史
    /// </summary>
    ChatHistory LLMChatHistory { get; }

    /// <summary>
    /// 开始对话异步方法
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="token">取消令牌</param>
    /// <returns>异步任务</returns>
    Task StartDialogueAsync(string userMessage,Session session, CancellationToken token);
}
}
