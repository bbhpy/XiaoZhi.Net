using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    /// <summary>
    /// MCP客户端类，继承自BaseProvider，实现了IMcpClient接口
    /// 负责管理多个子MCP客户端实例
    /// </summary>
    internal class McpClient : BaseProvider<McpClient, Dictionary<string, MCPClientBuildConfig>>, IMcpClient
    {
        /// <summary>
        /// 存储所有子MCP客户端的字典，键为子类型名称，值为对应的客户端实例
        /// </summary>
        private readonly Dictionary<string, ISubMcpClient> _subMcpClients = new Dictionary<string, ISubMcpClient>();

        /// <summary>
        /// 服务提供程序，用于获取依赖注入的服务实例
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 获取模型名称，返回当前类的名称
        /// </summary>
        public override string ModelName => nameof(McpClient);

        /// <summary>
        /// 获取提供者类型，返回"McpClient"
        /// </summary>
        public override string ProviderType => "McpClient";

        /// <summary>
        /// 初始化McpClient类的新实例
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        /// <param name="logger">日志记录器</param>
        public McpClient(IServiceProvider serviceProvider, ILogger<McpClient> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 获取所有子MCP客户端
        /// </summary>
        /// <returns>包含所有子MCP客户端的字典</returns>
        public IDictionary<string, ISubMcpClient> GetAllSubMcpClients() => this._subMcpClients;

        /// <summary>
        /// 根据子类型名称获取指定的子MCP客户端
        /// </summary>
        /// <param name="subTypeName">子类型名称</param>
        /// <returns>找到的子MCP客户端实例，如果未找到则返回null</returns>
        public ISubMcpClient? GetSubMcpClient(string subTypeName)
        {
            if (this._subMcpClients.TryGetValue(subTypeName, out var subMcpClient))
            {
                return subMcpClient;
            }
            else
            {
                this.Logger.LogError("SubMcpClient with type name '{subTypeName}' not found.", subTypeName);
                return null;
            }
        }

        /// <summary>
        /// 构建MCP客户端及其子客户端
        /// </summary>
        /// <param name="mcpSettings">MCP配置设置字典</param>
        /// <returns>构建是否成功的布尔值</returns>
        public override bool Build(Dictionary<string, MCPClientBuildConfig> mcpSettings)
        {
            ModelSetting defaultSetting = new ModelSetting();

            // DeviceMcpClient
            ISubMcpClient deviceMcpClient = this._serviceProvider.GetRequiredKeyedService<ISubMcpClient>(SubMCPClientTypeNames.DeviceMcpClient);
            this._subMcpClients.Add(SubMCPClientTypeNames.DeviceMcpClient, deviceMcpClient);

            // McpEndpointClient
            if (mcpSettings.ContainsKey(SubMCPClientTypeNames.McpEndpointClient))
            {
                ISubMcpClient mcpEndpointClient = this._serviceProvider.GetRequiredKeyedService<ISubMcpClient>(SubMCPClientTypeNames.McpEndpointClient);
                this._subMcpClients.Add(SubMCPClientTypeNames.McpEndpointClient, deviceMcpClient);
            }

            // ServerMcpClient
            if (mcpSettings.ContainsKey(SubMCPClientTypeNames.ServerMcpClient))
            {
                ISubMcpClient serverMcpClient = this._serviceProvider.GetRequiredKeyedService<ISubMcpClient>(SubMCPClientTypeNames.ServerMcpClient);
                this._subMcpClients.Add(SubMCPClientTypeNames.ServerMcpClient, serverMcpClient);
            }

            var buildResults = this._subMcpClients.Values
                .AsParallel()
                .Select(client => client.Build(mcpSettings[client.ModelName]))
                .ToArray();

            return buildResults.All(result => result);
        }

        /// <summary>
        /// 释放资源，清理所有子MCP客户端
        /// </summary>
        public override void Dispose()
        {
            foreach (var subMcpClient in this._subMcpClients.Values)
            {
                subMcpClient.Dispose();
            }

            this._subMcpClients.Clear();
        }
    }
}
