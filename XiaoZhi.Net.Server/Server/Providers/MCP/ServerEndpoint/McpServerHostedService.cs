using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// MCP Server 后台服务
    /// </summary>
    internal class McpServerHostedService : IHostedService
    {
        private readonly McpServerEndpoint _mcpServer;
        private readonly ILogger<McpServerHostedService> _logger;
        private readonly int _port;
        private readonly string _path;

        public McpServerHostedService(
            McpServerEndpoint mcpServer,
            ILogger<McpServerHostedService> logger,
            int port,
            string path)
        {
            _mcpServer = mcpServer;
            _logger = logger;
            _port = port;
            _path = path;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _mcpServer.StartAsync(_port, _path);
                _logger.LogInformation("MCP Server started on port {Port}, path: {Path}", _port, _path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start MCP Server");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _mcpServer.StopAsync();
            _logger.LogInformation("MCP Server stopped");
        }
    }
}
