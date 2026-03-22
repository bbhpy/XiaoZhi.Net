using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Providers.MCP.Events;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 设备上线处理器
    /// 订阅设备上线事件，触发三方工具注册
    /// </summary>
    internal class DeviceOnlineHandler : IDisposable
    {
        private readonly ILogger<DeviceOnlineHandler> _logger;
        private readonly ThirdPartyToolRegistrar _toolRegistrar;
        private readonly IEventPublisher _eventPublisher;
        private readonly IDisposable _subscription;

        public DeviceOnlineHandler(
            ILogger<DeviceOnlineHandler> logger,
            ThirdPartyToolRegistrar toolRegistrar,
            IEventPublisher eventPublisher)
        {
            _logger = logger;
            _toolRegistrar = toolRegistrar;
            _eventPublisher = eventPublisher;

            // 订阅设备上线事件
            _subscription = _eventPublisher.Subscribe<DeviceOnlineEvent>(OnDeviceOnline);

            _logger.LogInformation("DeviceOnlineHandler 已启动，订阅了设备上线事件");
        }

        /// <summary>
        /// 处理设备上线事件
        /// </summary>
        private async void OnDeviceOnline(DeviceOnlineEvent @event)
        {
            try
            {
                _logger.LogInformation("处理设备上线事件: 设备 {DeviceToken}, 会话 {SessionId}",
                    @event.DeviceToken, @event.SessionId);

                // 为该设备注册所有绑定的三方工具
                await _toolRegistrar.RegisterToolsForDeviceAsync(@event.DeviceToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理设备上线事件失败: 设备 {DeviceToken}", @event.DeviceToken);
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _logger.LogInformation("DeviceOnlineHandler 已释放");
        }
    }
}

