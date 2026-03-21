using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Common.Constants;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 工具注册表，管理工具与设备/服务的映射关系（仅用于路由）
    /// 完整工具定义存储在 McpServiceStore 中
    /// </summary>
    internal class ToolRegistry
    {
        private readonly ILogger<ToolRegistry> _logger;

        // 工具名 -> 该工具所属的所有(设备Token, 服务ID)列表（仅用于路由）
        private readonly ConcurrentDictionary<string, List<(string DeviceToken, string ServiceId)>> _toolMap = new();

        // 设备Token -> 该设备的所有WebSocket连接
        private readonly ConcurrentDictionary<string, List<WebSocketConnection>> _deviceConnections = new();

        // 等待响应的调用结果
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pendingCalls = new();

        private readonly McpServiceStore _serviceStore;
        /// <summary>
        /// 当设备的工具列表更新时触发的事件
        /// </summary>
        public event Func<string, string, Task>? OnToolsUpdated;

        private readonly Lazy<ThirdPartyToolRegistrar> _toolRegistrarLazy;
        public ToolRegistry(
            ILogger<ToolRegistry> logger,
            McpServiceStore serviceStore)  // 改为 Lazy 注入
        {
            _logger = logger;
            _serviceStore = serviceStore;
        }

        /// <summary>
        /// 注册工具（注册名称用于路由，完整定义存在ServiceBinding中）
        /// </summary>
        public void RegisterTool(string toolName, string deviceToken, string serviceId)
        {
            if (!StaticDeputy.IsValidToolName(toolName))
            {
                this._logger.LogWarning("工具名称 {ToolName} 包含非法字符，已跳过", toolName);
                return;
            }

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
        /// 获取设备绑定的所有三方工具（完整定义）
        /// </summary>
        public List<ToolDefinition> GetDeviceTools(string deviceToken)
        {
            var result = new List<ToolDefinition>();

            var bindings = _serviceStore.GetServicesByDevice(deviceToken);
            foreach (var binding in bindings)
            {
                if (binding.Tools != null && binding.Tools.Any())
                {
                    result.AddRange(binding.Tools);
                }
            }

            return result;
        }

        /// <summary>
        /// 调用三方工具
        /// </summary>
        public async Task<JsonObject> CallThirdPartyToolAsync(string toolName, KernelArguments arguments)
        {
            // 根据工具名找到对应的设备和连接
            var targets = FindToolTargets(toolName);
            if (!targets.Any())
                throw new Exception($"三方工具 {toolName} 不可用");

            var (deviceToken, serviceId, socket) = targets.First();

            // 检查socket状态
            if (socket == null || socket.State != WebSocketState.Open)
                throw new Exception($"三方工具 {toolName} 的 WebSocket 连接已关闭");

            // 创建调用ID
            int callId = GenerateCallId();

            // 注册等待的调用 - RegisterPendingCall 返回 Task<JsonObject>
            var task = RegisterPendingCall(callId); 

            // 转换参数
            var argsObject = new JsonObject();
            foreach (var arg in arguments)
            {
                if (arg.Value is string strValue && int.TryParse(strValue, out int intValue))
                    argsObject[arg.Key] = JsonValue.Create(intValue);
                else if (arg.Value != null)
                    argsObject[arg.Key] = JsonValue.Create(arg.Value);
            }

            // 发送调用请求
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = callId,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = toolName,
                    ["arguments"] = argsObject
                }
            };

            await SendWebSocketMessageAsync(socket, request);

            // 等待响应（带超时）
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(task, timeoutTask); 

            if (completedTask == timeoutTask)
                throw new TimeoutException($"调用三方工具 {toolName} 超时");

            return await task; 
        }

        /// <summary>
        /// 发送 WebSocket 消息
        /// </summary>
        private async Task SendWebSocketMessageAsync(WebSocket socket, JsonObject message)
        {
            var json = message.ToJsonString();
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }

        // 添加 GenerateCallId 方法
        private int _nextCallId = 1000;
        private int GenerateCallId()
        {
            return Interlocked.Increment(ref _nextCallId);
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