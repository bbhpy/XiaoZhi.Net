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
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Server.Common.Constants;
using XiaoZhi.Net.Server.Server.Providers.MCP.Events;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 管理单个三方MCP服务的连接（被动模式）
    /// 职责：MCP握手、请求工具列表、保存绑定、发布事件
    /// 不负责更新Kernel
    /// </summary>
    internal class McpServerConnection
    {
        private readonly WebSocket _webSocket;
        private readonly ILogger<McpServerConnection> _logger;
        private readonly McpServiceStore _serviceStore;
        private readonly McpConnectionManager _connectionManager;
        private readonly McpCallManager _callManager;
        private readonly IEventPublisher _eventPublisher;
        private readonly string _deviceToken;
        private readonly string _connectionId;
        private readonly bool _isValidToken;
        private readonly CancellationTokenSource _cts = new();

        // 服务标识
        private string? _serviceId = "pending";
        private string? _serviceName = "待识别服务";
        private bool _toolsReceived;

        public string ConnectionId => _connectionId;
        public string DeviceToken => _deviceToken;
        public string? ServiceId => _serviceId;
        public bool IsConnected => _webSocket.State == WebSocketState.Open;

        public McpServerConnection(
            WebSocket webSocket,
            ILogger<McpServerConnection> logger,
            McpServiceStore serviceStore,
            McpConnectionManager connectionManager,
            McpCallManager callManager,
            IEventPublisher eventPublisher,
            string deviceToken,
            string connectionId ,
            bool isValidToken)
        {
            _webSocket = webSocket;
            _logger = logger;
            _serviceStore = serviceStore;
            _connectionManager = connectionManager;
            _callManager = callManager;
            _eventPublisher = eventPublisher;
            _deviceToken = deviceToken;
            _connectionId = connectionId;
            _isValidToken = isValidToken;
        }

        /// <summary>
        /// 开始处理连接（阻塞直到连接关闭）
        /// </summary>
        public async Task HandleAsync()
        {
            try
            {
                // 创建临时绑定
                await CreateTemporaryBindingAsync();

                _logger.LogInformation("连接建立，开始MCP握手，设备Token: {Token}", _deviceToken);

                // 发送initialize请求并等待响应
                await InitiateHandshakeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP服务连接处理出错，设备Token: {Token}", _deviceToken);
            }
            finally
            {
                await CloseAsync("连接关闭");
            }
        }

        /// <summary>
        /// 发起MCP握手
        /// </summary>
        private async Task InitiateHandshakeAsync()
        {
            _logger.LogInformation("服务端主动发送 initialize 请求...");

            var initRequest = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject
                    {
                        ["experimental"] = new JsonObject(),
                        ["prompts"] = new JsonObject { ["listChanged"] = false },
                        ["tools"] = new JsonObject { ["listChanged"] = false }
                    },
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "XiaoZhi-MCP-Server",
                        ["version"] = "1.0.0"
                    }
                }
            };

            await SendJsonAsync(initRequest);
            _logger.LogInformation("initialize已发送，等待响应...");

            // 等待initialize响应
            var initResponse = await WaitForMessageAsync(1);
            _logger.LogInformation("收到 initialize 响应");

            // 发送initialized通知
            var notif = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await SendJsonAsync(notif);

            // 等待一小段时间确保服务端处理完成
            await Task.Delay(500);

            // 请求工具列表
            await RequestToolsListAsync();

            // 进入消息循环
            await HandleMessagesAsync();
        }

        /// <summary>
        /// 主动请求工具列表
        /// </summary>
        private async Task RequestToolsListAsync()
        {
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/list"
            };

            await SendJsonAsync(request);
            _logger.LogInformation("已向设备Token: {Token} 请求工具列表", _deviceToken);
        }

        /// <summary>
        /// 等待指定ID的响应消息
        /// </summary>
        private async Task<JsonObject> WaitForMessageAsync(int expectedId)
        {
            var buffer = new byte[4096];
            var timeout = TimeSpan.FromSeconds(10);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            cts.CancelAfter(timeout);

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new Exception("连接关闭");
                    }

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("等待响应时收到消息: {Json}", json);

                    var obj = JsonNode.Parse(json)?.AsObject();
                    if (obj == null) continue;

                    if (obj.TryGetPropertyValue("id", out var idNode))
                    {
                        var id = idNode.GetValue<int>();
                        if (id == expectedId)
                        {
                            return obj;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"等待消息ID {expectedId} 超时");
            }

            throw new Exception("未知错误");
        }

        /// <summary>
        /// 创建临时绑定
        /// </summary>
        private async Task CreateTemporaryBindingAsync()
        {
            var existingBinding = _serviceStore.GetBinding(_deviceToken, "pending");

            if (existingBinding == null)
            {
                var binding = new ServiceBinding
                {
                    DeviceToken = _deviceToken,
                    ServiceId = "pending",
                    ServiceName = "待识别服务",
                    Tools = new List<ToolDefinition>(),
                    FirstConnectedAt = DateTime.UtcNow,
                    LastConnectedAt = DateTime.UtcNow,
                    LastUpdatedAt = DateTime.UtcNow,
                    CurrentConnectionId = _connectionId
                };

                if (_serviceStore.SaveBinding(binding))
                {
                    _logger.LogInformation("创建临时绑定成功，设备Token: {Token}", _deviceToken);
                }
                else
                {
                    _logger.LogError("创建临时绑定失败，设备Token: {Token}", _deviceToken);
                }
            }
            else
            {
                existingBinding.LastConnectedAt = DateTime.UtcNow;
                existingBinding.CurrentConnectionId = _connectionId;
                existingBinding.LastUpdatedAt = DateTime.UtcNow;
                _serviceStore.SaveBinding(existingBinding);
                _logger.LogInformation("临时绑定更新成功，设备Token: {Token}", _deviceToken);
            }
        }

        /// <summary>
        /// 处理后续消息循环
        /// </summary>
        private async Task HandleMessagesAsync()
        {
            var buffer = new byte[4096];
            var messageBuffer = new StringBuilder();

            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuffer.Append(chunk);

                    if (result.EndOfMessage)
                    {
                        var completeMessage = messageBuffer.ToString();
                        messageBuffer.Clear();

                        try
                        {
                            await HandleMessageAsync(completeMessage);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "处理完整消息出错: {Message}", completeMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "接收消息出错，设备Token: {Token}", _deviceToken);
                    break;
                }
            }
        }

        /// <summary>
        /// 处理单条消息
        /// </summary>
        private async Task HandleMessageAsync(string json)
        {
            try
            {
                _logger.LogDebug("收到完整消息: {Json}", json);

                var obj = JsonNode.Parse(json)?.AsObject();
                if (obj == null) return;

                // 处理响应（有id的消息）
                if (obj.TryGetPropertyValue("id", out var idNode))
                {
                    var id = idNode.GetValue<int>();

                    // 处理 tools/list 响应
                    if (id == 2 && obj.TryGetPropertyValue("result", out var resultNode))
                    {
                        _logger.LogInformation("收到 tools/list 响应");
                        await HandleToolsListResponseAsync(resultNode.AsObject());
                        return;
                    }

                    // 处理工具调用响应
                    if (obj.TryGetPropertyValue("result", out var callResult))
                    {
                        _logger.LogDebug("收到工具调用结果，ID: {Id}", id);
                        _callManager.CompletePendingCall(id, obj);
                        return;
                    }

                    // 处理错误响应
                    if (obj.TryGetPropertyValue("error", out var errorNode))
                    {
                        _logger.LogError("收到错误响应，ID: {Id}, 错误: {Error}", id, errorNode.ToJsonString());
                        _callManager.RejectPendingCall(id, errorNode["message"]?.GetValue<string>() ?? "未知错误");
                        return;
                    }
                }

                // 处理请求（有method的消息）- 三方服务主动调用工具（暂不支持）
                if (obj.TryGetPropertyValue("method", out var methodNode))
                {
                    var method = methodNode.GetValue<string>();
                    _logger.LogDebug("收到请求: {Method}，暂不支持", method);

                    // 返回不支持响应
                    var errorResponse = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = obj["id"]?.GetValue<int>(),
                        ["error"] = new JsonObject
                        {
                            ["code"] = -32601,
                            ["message"] = "Method not supported"
                        }
                    };
                    await SendJsonAsync(errorResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息出错：{Json}", json);
            }
        }

        /// <summary>
        /// 处理工具列表响应
        /// </summary>
        private async Task HandleToolsListResponseAsync(JsonObject result)
        {
            try
            {
                _logger.LogInformation("开始解析工具列表...");

                string? realServiceId = null;
                string? realServiceName = null;

                // 从 server_info 获取服务标识
                if (result.TryGetPropertyValue("serverInfo", out var serverInfo) &&
                    serverInfo is JsonObject serverObj)
                {
                    realServiceId = serverObj["name"]?.GetValue<string>();
                    realServiceName = serverObj["description"]?.GetValue<string>() ?? serverObj["name"]?.GetValue<string>();
                    _logger.LogInformation("从 serverInfo 获取到服务标识: {ServiceId}", realServiceId);
                }

                // 解析工具列表
                var tools = new List<ToolDefinition>();
                if (result.TryGetPropertyValue("tools", out var toolsNode) &&
                    toolsNode is JsonArray toolsArray)
                {
                    foreach (var tool in toolsArray)
                    {
                        if (tool is JsonObject toolObj)
                        {
                            var toolDef = ToolDefinition.FromJson(toolObj);
                            if (!string.IsNullOrEmpty(toolDef.Name))
                            {
                                tools.Add(toolDef);
                                _logger.LogDebug("   - 工具：{ToolName}，描述：{Description}",
                                    toolDef.Name, toolDef.Description ?? "无描述");
                            }
                        }
                    }
                }

                // 确定服务ID
                if (string.IsNullOrEmpty(realServiceId))
                {
                    realServiceId = tools.Count > 0
                        ? tools[0].Name.Split('.').FirstOrDefault() ?? "unknown"
                        : "unknown";
                    realServiceName = realServiceName ?? $"{realServiceId}服务";
                }

                // 更新服务标识
                _serviceId = realServiceId;
                _serviceName = realServiceName;

                // 删除临时绑定
                _serviceStore.DeleteBinding(_deviceToken, "pending");

                // 创建正式绑定
                var binding = new ServiceBinding
                {
                    DeviceToken = _deviceToken,
                    ServiceId = _serviceId,
                    ServiceName = _serviceName ?? _serviceId,
                    Tools = tools,
                    FirstConnectedAt = DateTime.UtcNow,
                    LastConnectedAt = DateTime.UtcNow,
                    LastToolsUpdateAt = DateTime.UtcNow,
                    LastUpdatedAt = DateTime.UtcNow,
                    CurrentConnectionId = _connectionId
                };

                _serviceStore.SaveBinding(binding);

                // 注册连接到连接管理器
                _connectionManager.RegisterConnection(_deviceToken, _serviceId, _webSocket, _connectionId);

                _logger.LogInformation("服务识别完成：{ServiceId}({ServiceName})，注册了 {ToolCount} 个工具，Token状态: {IsValid}",
                    _serviceId, _serviceName, tools.Count, _isValidToken ? "有效" : "无效（待设备上线）");

                _toolsReceived = true;

                // ✅ 只有token有效时，才发布服务绑定事件
                if (_isValidToken)
                {
                    _eventPublisher.Publish(new ServiceBoundEvent(
                        _deviceToken,
                        _serviceId,
                        _serviceName ?? _serviceId,
                        DateTime.UtcNow));

                    _logger.LogInformation("已发布 ServiceBoundEvent，设备Token: {Token}, 服务: {ServiceId}",
                        _deviceToken, _serviceId);
                }
                else
                {
                    _logger.LogInformation("Token无效，服务 {ServiceId} 的工具已保存，等待设备上线后自动注册",
                        _serviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理工具列表响应出错，设备Token: {Token}", _deviceToken);
            }
        }

        /// <summary>
        /// 发送JSON消息
        /// </summary>
        private async Task SendJsonAsync(JsonObject json)
        {
            try
            {
                var jsonString = json.ToJsonString();
                _logger.LogDebug("发送消息: {Json}", jsonString);

                var bytes = Encoding.UTF8.GetBytes(jsonString);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送消息出错，设备Token: {Token}", _deviceToken);
            }
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public async Task CloseAsync(string reason)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        reason,
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "关闭连接出错，设备Token: {Token}", _deviceToken);
            }
            finally
            {
                _webSocket.Dispose();
                _cts.Cancel();

                if (_serviceId != null && _serviceId != "pending")
                {
                    _connectionManager.RemoveConnection(_deviceToken, _serviceId);

                    // 发布解绑事件
                    _eventPublisher.Publish(new ServiceUnboundEvent(_deviceToken, _serviceId));

                    _logger.LogInformation("连接已关闭，服务：{ServiceId}，设备Token: {Token}",
                        _serviceId, _deviceToken);
                }
                else
                {
                    _serviceStore.DeleteBinding(_deviceToken, "pending");
                    _logger.LogInformation("临时连接已关闭，设备Token: {Token}", _deviceToken);
                }
            }
        }
    }
}