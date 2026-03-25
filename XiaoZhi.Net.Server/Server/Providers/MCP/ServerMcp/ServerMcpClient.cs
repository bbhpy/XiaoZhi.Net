using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.SemanticKernel.Extensions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
    /// <summary>
    /// 服务器MCP客户端类，继承自BaseMcpClient并实现ISubMcpClient接口
    /// </summary>
    internal class ServerMcpClient : BaseMcpClient<ServerMcpClient>, ISubMcpClient
    {
        private bool _isInitialized = false;

        /// <summary>
        /// 初始化ServerMcpClient实例
        /// </summary>
        /// <param name="logger">用于日志记录的ILogger实例</param>
        public ServerMcpClient(ILogger<ServerMcpClient> logger, ToolRouter toolRegistry, McpServiceStore mcpServiceStore) : base(logger, toolRegistry, mcpServiceStore)
        {
        }

        /// <summary>
        /// 获取模型名称
        /// </summary>
        public override string ModelName => SubMCPClientTypeNames.ServerMcpClient;

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

                    // 获取当前会话的 Kernel
                    var kernel = this.CurrentSession.PrivateProvider?.Kernel;

                    if (kernel == null)
                    {
                        this.Logger.LogError("Kernel is null, cannot register MCP tools");
                        return false;
                    }

                    // 使用新包一行代码注册所有 MCP 工具
                    kernel.Plugins.AddMcpFunctionsFromStdioServerAsync(
                        serverName: name ?? "MCP Server",
                        command: command,
                        arguments: arguments.ToArray()
                    ).GetAwaiter().GetResult();

                    this.Logger.LogInformation($"MCP Server '{name}' registered successfully to kernel");
                    _isInitialized = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build ServerMcpClient");
                return false;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            // 新包不需要手动释放资源
            _isInitialized = false;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 异步发送MCP消息
        /// 注意：新包封装了通信，此方法不再需要实际发送消息
        /// 保留方法以符合接口要求
        /// </summary>
        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            // 新包内部处理了所有通信，此方法不再需要实现
            // 如果未来需要发送自定义消息，可在此实现
            return Task.CompletedTask;
        }

    }
}
