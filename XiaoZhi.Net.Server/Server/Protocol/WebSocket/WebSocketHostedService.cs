using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.WebSocket
{
    /// <summary>
    /// WebSocket 服务器后台服务
    /// </summary>
    internal class WebSocketHostedService : IHostedService
    {
        private readonly WebSocketServer _server;
        private readonly ILogger<WebSocketHostedService> _logger;

        public WebSocketHostedService(WebSocketServer server, ILogger<WebSocketHostedService> logger)
        {
            _server = server;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("启动 WebSocket 服务器...");
            await _server.StartAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("停止 WebSocket 服务器...");
            await _server.StopAsync();
        }
    }
}