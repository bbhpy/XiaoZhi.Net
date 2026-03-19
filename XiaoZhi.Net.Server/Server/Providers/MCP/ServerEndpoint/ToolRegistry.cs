using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 工具注册表，管理工具与设备/服务的映射关系（仅用于路由）
    /// 完整工具定义存储在 McpServiceStore 中
    /// </summary>
    public class ToolRegistry
    {
        private readonly ILogger<ToolRegistry> _logger;

        // 工具名 -> 该工具所属的所有(设备Token, 服务ID)列表（仅用于路由）
        private readonly ConcurrentDictionary<string, List<(string DeviceToken, string ServiceId)>> _toolMap = new();

        // 设备Token -> 该设备的所有WebSocket连接
        private readonly ConcurrentDictionary<string, List<WebSocketConnection>> _deviceConnections = new();

        // 等待响应的调用结果
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pendingCalls = new();

        public ToolRegistry(ILogger<ToolRegistry> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 注册工具（仅注册名称用于路由，完整定义存在ServiceBinding中）
        /// </summary>
        public void RegisterTool(string toolName, string deviceToken, string serviceId)
        {
            var list = _toolMap.GetOrAdd(toolName, _ => new List<(string, string)>());

            lock (list)
            {
                if (!list.Any(x => x.DeviceToken == deviceToken && x.ServiceId == serviceId))
                {
                    list.Add((deviceToken, serviceId));
                    _logger.LogDebug("工具路由注册: {ToolName} -> 设备 {DeviceToken}, 服务 {ServiceId}",
                        toolName, deviceToken, serviceId);
                }
            }
        }

        /// <summary>
        /// 注册设备连接
        /// </summary>
        public void RegisterConnection(string deviceToken, string serviceId, WebSocket socket, string connectionId)
        {
            var connections = _deviceConnections.GetOrAdd(deviceToken, _ => new List<WebSocketConnection>());

            lock (connections)
            {
                // 检查是否已存在相同服务ID的连接
                var existing = connections.FirstOrDefault(x => x.ServiceId == serviceId);
                if (existing != null)
                {
                    // 更新现有连接
                    existing.Socket = socket;
                    existing.ConnectionId = connectionId;
                    existing.LastActive = DateTime.UtcNow;
                    _logger.LogDebug("更新设备连接: {DeviceToken}, 服务 {ServiceId}, 连接 {ConnectionId}",
                        deviceToken, serviceId, connectionId);
                }
                else
                {
                    // 添加新连接
                    connections.Add(new WebSocketConnection
                    {
                        DeviceToken = deviceToken,
                        ServiceId = serviceId,
                        Socket = socket,
                        ConnectionId = connectionId,
                        CreatedAt = DateTime.UtcNow,
                        LastActive = DateTime.UtcNow
                    });
                    _logger.LogDebug("注册设备连接: {DeviceToken}, 服务 {ServiceId}, 连接 {ConnectionId}",
                        deviceToken, serviceId, connectionId);
                }
            }
        }

        /// <summary>
        /// 移除设备连接
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
                        _logger.LogDebug("移除设备连接: {DeviceToken}, 服务 {ServiceId}", deviceToken, serviceId);
                    }

                    if (connections.Count == 0)
                    {
                        _deviceConnections.TryRemove(deviceToken, out _);
                    }
                }
            }
        }

        /// <summary>
        /// 根据工具名查找所有可用的设备和连接
        /// </summary>
        public List<(string DeviceToken, string ServiceId, WebSocket Socket)> FindToolTargets(string toolName)
        {
            var result = new List<(string, string, WebSocket)>();

            if (_toolMap.TryGetValue(toolName, out var targets))
            {
                foreach (var (deviceToken, serviceId) in targets)
                {
                    if (_deviceConnections.TryGetValue(deviceToken, out var connections))
                    {
                        lock (connections)
                        {
                            var conn = connections.FirstOrDefault(x => x.ServiceId == serviceId);
                            if (conn != null && conn.Socket.State == WebSocketState.Open)
                            {
                                result.Add((deviceToken, serviceId, conn.Socket));
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 根据设备Token获取该设备的所有连接
        /// </summary>
        public List<WebSocketConnection> GetDeviceConnections(string deviceToken)
        {
            if (_deviceConnections.TryGetValue(deviceToken, out var connections))
            {
                lock (connections)
                {
                    return connections.ToList();
                }
            }
            return new List<WebSocketConnection>();
        }

        /// <summary>
        /// 注册等待的调用
        /// </summary>
        public Task<JsonObject> RegisterPendingCall(int callId)
        {
            var tcs = new TaskCompletionSource<JsonObject>();
            _pendingCalls[callId] = tcs;
            return tcs.Task;
        }

        /// <summary>
        /// 完成等待的调用
        /// </summary>
        public bool CompletePendingCall(int callId, JsonObject result)
        {
            if (_pendingCalls.TryRemove(callId, out var tcs))
            {
                tcs.SetResult(result);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 移除设备的所有工具注册（设备断开时调用）
        /// </summary>
        public void UnregisterDeviceTools(string deviceToken)
        {
            foreach (var toolName in _toolMap.Keys.ToList())
            {
                if (_toolMap.TryGetValue(toolName, out var list))
                {
                    lock (list)
                    {
                        list.RemoveAll(x => x.DeviceToken == deviceToken);
                    }
                }
            }

            _deviceConnections.TryRemove(deviceToken, out _);
            _logger.LogDebug("移除设备所有路由注册: {DeviceToken}", deviceToken);
        }

        /// <summary>
        /// WebSocket连接信息
        /// </summary>
        public class WebSocketConnection
        {/// <summary>
         /// 表示设备连接信息的实体类
         /// </summary>
            public string DeviceToken { get; set; }
            /// <summary>
            /// 获取或设置服务标识符
            /// </summary>
            public string ServiceId { get; set; }
            /// <summary>
            /// 获取或设置WebSocket连接对象
            /// </summary>
            public WebSocket Socket { get; set; }
            /// <summary>
            /// 获取或设置连接标识符
            /// </summary>
            public string ConnectionId { get; set; }
            /// <summary>
            /// 获取或设置连接创建时间
            /// </summary>
            public DateTime CreatedAt { get; set; }
            /// <summary>
            /// 获取或设置最后活跃时间
            /// </summary>
            public DateTime LastActive { get; set; }
        }
    }
}