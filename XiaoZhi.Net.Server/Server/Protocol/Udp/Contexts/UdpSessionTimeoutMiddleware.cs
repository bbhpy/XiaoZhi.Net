using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP 会话超时清理中间件
    /// 对标 WebSocket 心跳检测、Session 超时下线
    /// </summary>
    internal class UdpSessionTimeoutMiddleware : BackgroundService
    {
        private readonly MqttUdpSessionStore _sessionManager;
        private readonly ILogger<UdpSessionTimeoutMiddleware> _logger;

        // 新增：可外部赋值的超时时间属性
        public int SessionTimeoutSeconds { get; set; } = 60;

        public UdpSessionTimeoutMiddleware(
            MqttUdpSessionStore sessionManager,
            ILogger<UdpSessionTimeoutMiddleware> logger)
        {
            _sessionManager = sessionManager;
            _logger = logger;
        }

        // 核心清理逻辑直接调用 sessionStore.CleanupTimeoutSessions()
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    //int removed = _sessionManager.ud(SessionTimeoutSeconds);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理超时会话失败");
                }
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
