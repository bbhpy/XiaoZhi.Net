using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 三方服务WebSocket连接管理器
    /// 负责管理(设备Token, 服务ID) -> WebSocket连接的映射
    /// </summary>
    internal class McpConnectionManager
    {
        private readonly ILogger<McpConnectionManager> _logger;

        // 设备Token -> 该设备的所有连接
        private readonly ConcurrentDictionary<string, List<ServiceConnection>> _deviceConnections = new();

        public McpConnectionManager(ILogger<McpConnectionManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 注册服务连接
        /// </summary>
        public void RegisterConnection(string deviceToken, string serviceId, WebSocket socket, string connectionId)
        {
            var connections = _deviceConnections.GetOrAdd(deviceToken, _ => new List<ServiceConnection>());

            lock (connections)
            {
                var existing = connections.FirstOrDefault(x => x.ServiceId == serviceId);
                if (existing != null)
                {
                    // 更新现有连接
                    existing.Socket = socket;
                    existing.ConnectionId = connectionId;
                    existing.LastActive = DateTime.UtcNow;
                    _logger.LogDebug("更新服务连接: {DeviceToken}, 服务 {ServiceId}, 连接 {ConnectionId}",
                        deviceToken, serviceId, connectionId);
                }
                else
                {
                    // 添加新连接
                    connections.Add(new ServiceConnection
                    {
                        DeviceToken = deviceToken,
                        ServiceId = serviceId,
                        Socket = socket,
                        ConnectionId = connectionId,
                        CreatedAt = DateTime.UtcNow,
                        LastActive = DateTime.UtcNow
                    });
                    _logger.LogDebug("注册服务连接: {DeviceToken}, 服务 {ServiceId}, 连接 {ConnectionId}",
                        deviceToken, serviceId, connectionId);
                }
            }
        }

        /// <summary>
        /// 移除服务连接
        /// </summary>
        public void RemoveConnection(string deviceToken, string serviceId)
        {
            if (_deviceConnections.TryGetValue(deviceToken, out var connections))
            {
                lock (connections)
                {
                    var removed = connections.RemoveAll(x => x.ServiceId == serviceId);
                    if (removed > 0)
                    {
                        _logger.LogDebug("移除服务连接: {DeviceToken}, 服务 {ServiceId}", deviceToken, serviceId);
                    }

                    if (connections.Count == 0)
                    {
                        _deviceConnections.TryRemove(deviceToken, out _);
                    }
                }
            }
        }

        /// <summary>
        /// 移除设备的所有连接
        /// </summary>
        public void RemoveDeviceConnections(string deviceToken)
        {
            if (_deviceConnections.TryRemove(deviceToken, out var connections))
            {
                lock (connections)
                {
                    _logger.LogDebug("移除设备 {DeviceToken} 的所有 {Count} 个连接", deviceToken, connections.Count);
                }
            }
        }

        /// <summary>
        /// 获取服务连接
        /// </summary>
        public WebSocket? GetConnection(string deviceToken, string serviceId)
        {
            if (_deviceConnections.TryGetValue(deviceToken, out var connections))
            {
                lock (connections)
                {
                    var conn = connections.FirstOrDefault(x => x.ServiceId == serviceId);
                    if (conn != null && conn.Socket?.State == WebSocketState.Open)
                    {
                        conn.LastActive = DateTime.UtcNow;
                        return conn.Socket;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 获取设备的所有在线服务连接
        /// </summary>
        public List<ServiceConnection> GetDeviceConnections(string deviceToken)
        {
            if (_deviceConnections.TryGetValue(deviceToken, out var connections))
            {
                lock (connections)
                {
                    return connections
                        .Where(x => x.Socket?.State == WebSocketState.Open)
                        .ToList();
                }
            }
            return new List<ServiceConnection>();
        }

        /// <summary>
        /// 检查服务是否在线
        /// </summary>
        public bool IsServiceOnline(string deviceToken, string serviceId)
        {
            return GetConnection(deviceToken, serviceId) != null;
        }

        /// <summary>
        /// 异步发送消息到指定服务
        /// </summary>
        public async Task<bool> SendMessageAsync(string deviceToken, string serviceId, JsonObject message, CancellationToken cancellationToken = default)
        {
            var socket = GetConnection(deviceToken, serviceId);
            if (socket == null)
            {
                _logger.LogWarning("发送消息失败: 设备 {DeviceToken} 服务 {ServiceId} 不在线", deviceToken, serviceId);
                return false;
            }

            try
            {
                var json = message.ToJsonString();
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送消息到设备 {DeviceToken} 服务 {ServiceId} 失败", deviceToken, serviceId);
                return false;
            }
        }

        /// <summary>
        /// 服务连接信息
        /// </summary>
        public class ServiceConnection
        {
            public string DeviceToken { get; set; } = string.Empty;
            public string ServiceId { get; set; } = string.Empty;
            public WebSocket? Socket { get; set; }
            public string ConnectionId { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime LastActive { get; set; }
        }
    }
}

