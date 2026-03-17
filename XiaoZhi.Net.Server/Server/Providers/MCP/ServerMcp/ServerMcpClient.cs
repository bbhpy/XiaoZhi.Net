using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
/// <summary>
/// 服务器MCP客户端类，继承自BaseMcpClient并实现ISubMcpClient接口
/// </summary>
internal class ServerMcpClient : BaseMcpClient<ServerMcpClient>, ISubMcpClient
{
    private ModelContextProtocol.Client.IMcpClient? _mcpClient;

    /// <summary>
    /// 初始化ServerMcpClient实例
    /// </summary>
    /// <param name="logger">用于日志记录的ILogger实例</param>
    public ServerMcpClient(ILogger<ServerMcpClient> logger) : base(logger)
    {
    }

    /// <summary>
    /// 获取模型名称
    /// </summary>
    public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
    
    /// <summary>
    /// 获取提供者类型
    /// </summary>
    public override string ProviderType => "SubMcpClient";

    /// <summary>
    /// 构建MCP客户端
    /// </summary>
    /// <param name="config">MCP客户端构建配置</param>
    /// <returns>构建是否成功</returns>
    public override bool Build(MCPClientBuildConfig config)
    {
        try
        {
            this.InitSession(config);
            ModelSetting modelSetting = config.ModelSetting;

            if (this.ModelName.ToLower() == "stdio-client")
            {
                string? name = modelSetting.Config.GetConfigValueOrDefault("Name");
                string command = modelSetting.Config.GetConfigValueOrDefault("Command", string.Empty);
                List<string> arguments = modelSetting.Config.GetConfigValueOrDefault("Arguments", new List<string>());
                
                // 创建 ServiceCollection 并添加日志记录器
                var serviceCollection = new ServiceCollection();
                serviceCollection.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddConsole();
                });

                // 构建 IServiceProvider
                var serviceProvider = serviceCollection.BuildServiceProvider();
                var logger = serviceProvider.GetRequiredService<ILogger<McpClient>>();
                var transport = new StdioClientTransport(new()
                {
                    Name = name,
                    Command = command,
                    Arguments = arguments
                });

                this._mcpClient = McpClientFactory.CreateAsync(transport).GetAwaiter().GetResult();
                IList<McpClientTool> tools = this._mcpClient.ListToolsAsync().GetAwaiter().GetResult();
                foreach (McpClientTool tool in tools)
                {
                    this.Logger.LogInformation($"Got mcp tools: {tool.Name}, Description: {tool.Description}");
#pragma warning disable SKEXP0001
                    this.AddTool(tool.Name, tool.AsKernelFunction());
#pragma warning restore SKEXP0001
                }
            }


            return true;
        }
        catch (Exception)
        {

            throw;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        this._mcpClient?.DisposeAsync();
    }

    /// <summary>
    /// 异步发送MCP消息
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="message">要发送的消息</param>
    /// <returns>异步任务</returns>
    protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
    {
        // todo
        return Task.CompletedTask;
    }

}
}
