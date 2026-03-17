using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.MCP
{
/// <summary>
/// MCP客户端接口，定义了与MCP（Model Context Protocol）服务交互的基本功能
/// </summary>
internal interface ISubMcpClient : IProvider<MCPClientBuildConfig>
{
    /// <summary>
    /// 获取可用的内核函数集合
    /// </summary>
    ICollection<KernelFunction> Functions { get; }

    /// <summary>
    /// 获取客户端是否已准备就绪的状态
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// 获取下一个可用的ID
    /// </summary>
    int NextId { get; }

    /// <summary>
    /// 检查是否存在指定名称的工具
    /// </summary>
    /// <param name="toolName">要检查的工具名称</param>
    /// <returns>如果存在则返回true，否则返回false</returns>
    bool HasTool(string toolName);

    /// <summary>
    /// 异步处理MCP消息
    /// </summary>
    /// <param name="jsonObject">包含MCP消息的JSON对象</param>
    /// <returns>异步操作任务</returns>
    Task HandleMcpMessageAsync(JsonObject jsonObject);

    /// <summary>
    /// 异步发送MCP初始化请求
    /// </summary>
    /// <returns>异步操作任务</returns>
    Task SendMcpInitializeAsync();

    /// <summary>
    /// 异步发送MCP通知
    /// </summary>
    /// <param name="method">通知方法名称</param>
    /// <returns>异步操作任务</returns>
    Task SendMcpNotificationAsync(string method);

    /// <summary>
    /// 异步请求工具列表
    /// </summary>
    /// <returns>异步操作任务</returns>
    Task RequestToolsListAsync();

    /// <summary>
    /// 异步请求工具列表（支持分页）
    /// </summary>
    /// <param name="cursor">分页游标</param>
    /// <returns>异步操作任务</returns>
    Task RequestToolsListAsync(string cursor);

    /// <summary>
    /// 异步调用MCP工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="timeout">超时时间（秒），默认30秒</param>
    /// <returns>工具执行结果字符串</returns>
    Task<string> CallMcpToolAsync(string toolName, KernelArguments arguments, int timeout = 30);
}
}
