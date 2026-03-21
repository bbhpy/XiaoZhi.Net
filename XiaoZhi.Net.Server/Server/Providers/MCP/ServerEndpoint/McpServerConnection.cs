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

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 管理单个三方MCP服务的连接（被动模式）
    /// </summary>
    internal class McpServerConnection
    {
        private readonly WebSocket _webSocket;
        private readonly ILogger<McpServerConnection> _logger;
        private readonly McpServiceStore _serviceStore;
        private readonly ToolRegistry _toolRegistry;
        private readonly string _deviceToken;
        private readonly CancellationTokenSource _cts = new();

        // 服务标识（给程序用的，唯一）
        private string? _serviceId = "pending";
        // 服务名称（给人看的，显示用）
        private string? _serviceName = "待识别服务";
        private bool _toolsReceived;

        // 等待响应的调用
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pendingCalls = new();

        public string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public string DeviceToken => _deviceToken;
        public string? ServiceId => _serviceId;
        public bool IsConnected => _webSocket.State == WebSocketState.Open;

        public McpServerConnection(
            WebSocket webSocket,
            ILogger<McpServerConnection> logger,
            McpServiceStore serviceStore,
            ToolRegistry toolRegistry,  // 新增
            string deviceToken)
        {
            _webSocket = webSocket;
            _logger = logger;
            _serviceStore = serviceStore;
            _toolRegistry = toolRegistry;
            _deviceToken = deviceToken;
        }

        /// <summary>
        /// 开始处理连接（阻塞直到连接关闭）
        /// </summary>
        public async Task HandleAsync()
        {
            try
            {
                await CreateTemporaryBindingAsync();

                _logger.LogInformation("连接建立，立即检查是否有消息...");

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
                        ["prompts"] = new JsonObject
                        {
                            ["listChanged"] = false
                        },
                        ["tools"] = new JsonObject
                        {
                            ["listChanged"] = false
                        }
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

            var initResponse = await WaitForMessageAsync(1);
            _logger.LogInformation("收到 initialize 响应");

            var capabilities = initResponse?["result"]?["capabilities"]?.AsObject();
            _logger.LogInformation("HA capabilities: {Capabilities}", capabilities?.ToJsonString());

            var notif = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await SendJsonAsync(notif);

            await Task.Delay(1000);

            await RequestToolsListAsync();
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
            _logger.LogInformation("📋 已向设备Token: {Token} 请求工具列表", _deviceToken);
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
                    _logger.LogInformation("等待响应时收到消息: {Json}", json);

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
            var bindingKey = $"binding:{_deviceToken}:pending";

            var binding = _serviceStore.SafeGetBinding(bindingKey);

            if (binding == null)
            {
                binding = new ServiceBinding
                {
                    DeviceToken = _deviceToken,
                    ServiceId = "pending",
                    ServiceName = "待识别服务",
                    Tools = new List<ToolDefinition>(),
                    FirstConnectedAt = DateTime.UtcNow,
                    LastConnectedAt = DateTime.UtcNow,
                    CurrentConnectionId = ConnectionId
                };

                if (_serviceStore.Add(bindingKey, binding))
                {
                    _logger.LogInformation("✅ 创建临时绑定成功，设备Token: {Token}", _deviceToken);
                }
                else
                {
                    _logger.LogError("❌ 创建临时绑定失败，设备Token: {Token}", _deviceToken);
                }
            }
            else
            {
                binding.LastConnectedAt = DateTime.UtcNow;
                binding.CurrentConnectionId = ConnectionId;

                if (_serviceStore.Update(bindingKey, binding))
                {
                    _logger.LogInformation("🔄 临时绑定更新成功，设备Token: {Token}", _deviceToken);
                }
                else
                {
                    _logger.LogError("❌ 临时绑定更新失败，设备Token: {Token}", _deviceToken);
                }
            }
        }

        /// <summary>
        /// 处理后续消息
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

                    // 追加到缓冲区
                    messageBuffer.Append(chunk);

                    // 检查消息是否完整（检查是否以 } 结尾且JSON格式正确）
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

        private async Task HandleMessageAsync(string json)
        {
            try
            {
                _logger.LogInformation("📥 收到完整消息: {Json}", json);

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
                        _logger.LogInformation("收到工具调用结果，ID: {Id}", id);
                        // 通知等待调用的地方
                        _toolRegistry.CompletePendingCall(id, obj);
                        return;
                    }

                    // 处理错误响应
                    if (obj.TryGetPropertyValue("error", out var errorNode))
                    {
                        _logger.LogError("收到错误响应，ID: {Id}, 错误: {Error}", id, errorNode.ToJsonString());
                        _toolRegistry.CompletePendingCall(id, obj);
                        return;
                    }
                }

                // 处理请求（有method的消息）
                if (obj.TryGetPropertyValue("method", out var methodNode))
                {
                    var method = methodNode.GetValue<string>();
                    _logger.LogInformation("收到请求: {Method}", method);

                    if (method == "tools/call")
                    {
                        await HandleToolCallAsync(obj);
                        return;
                    }
                }

                _logger.LogDebug("收到未处理的消息：{Json}", json);
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
                if (result.TryGetPropertyValue("server_info", out var serverInfo) &&
                    serverInfo is JsonObject serverObj)
                {
                    realServiceId = serverObj["name"]?.GetValue<string>();
                    realServiceName = serverObj["description"]?.GetValue<string>();
                    _logger.LogInformation("从 server_info 获取到服务标识: {ServiceId}", realServiceId);
                }

                // 解析工具列表
                if (result.TryGetPropertyValue("tools", out var toolsNode) &&
                    toolsNode is JsonArray toolsArray)
                {
                    var tools = new List<ToolDefinition>();

                    foreach (var tool in toolsArray)
                    {
                        if (tool is JsonObject toolObj)
                        {
                            var toolDef = ToolDefinition.FromJson(toolObj);

                            if (!StaticDeputy.IsValidToolName(toolDef.Name))
                            {
                                this._logger.LogWarning("工具名称 {ToolName} 包含非法字符，已跳过", toolDef.Name);
                                continue;
                            }

                            tools.Add(toolDef);

                            _logger.LogDebug("   - 工具：{ToolName}，描述：{Description}",
                                toolDef.Name, toolDef.Description ?? "无描述");

                            // ⭐ 注册工具到全局索引（只注册名称用于快速路由）
                            // 注意：这里会触发 ThirdPartyToolRegistrar 注册到 Kernel
                            _toolRegistry.RegisterTool(toolDef.Name, _deviceToken, _serviceId ?? "unknown");
                        }
                    }

                    // 确定服务ID
                    if (string.IsNullOrEmpty(realServiceId))
                    {
                        realServiceId = tools.Count > 0 ? tools[0].Name.Split('.').FirstOrDefault() ?? "unknown" : "unknown";
                        realServiceName = $"{realServiceId}服务";
                    }

                    // 更新服务标识
                    _serviceId = realServiceId;
                    _serviceName = realServiceName;

                    // 更新绑定信息
                    var tempKey = $"binding:{_deviceToken}:pending";
                    var newKey = $"binding:{_deviceToken}:{_serviceId}";

                    var binding = _serviceStore.Get<ServiceBinding>(tempKey);
                    if (binding != null)
                    {
                        _serviceStore.Remove(tempKey);

                        binding.ServiceId = _serviceId;
                        binding.ServiceName = _serviceName ?? _serviceId;
                        binding.Tools = tools;  // 存储完整工具定义
                        _serviceStore.Add(newKey, binding);

                        // 注册连接
                        _toolRegistry.RegisterConnection(_deviceToken, _serviceId, _webSocket, ConnectionId);

                        _logger.LogInformation("✅ 服务识别完成：{ServiceId}({ServiceName})，注册了 {ToolCount} 个工具",
                            _serviceId, _serviceName, tools.Count);
                    }

                    _toolsReceived = true;

                    _logger.LogInformation("三方工具已注册，等待设备 Kernel 更新...");
                }
                else
                {
                    _logger.LogWarning("tools/list 响应中没有找到 tools 数组");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理工具列表响应出错，设备Token: {Token}", _deviceToken);
            }
        }

        /// <summary>
        /// 处理工具调用（从小智服务端转发过来的请求）
        /// </summary>
        private async Task HandleToolCallAsync(JsonObject request)
        {
            var id = request["id"]?.GetValue<int>();
            var @params = request["params"]?.AsObject();
            var toolName = @params?["name"]?.GetValue<string>();
            var arguments = @params?["arguments"]?.AsObject();

            _logger.LogInformation("🔧 收到工具调用请求：{ToolName}，参数：{Arguments}",
                toolName, arguments?.ToJsonString());

            try
            {
                // 这里不需要实现具体逻辑，因为这是从三方服务收到的请求
                // 实际上，当三方服务（如HA）主动调用工具时，这个请求才会进来
                // 但现在我们是作为Server接收HA的连接，所以正常情况下HA不会主动调用

                // 返回成功响应（实际应该根据工具名调用对应的功能）
                var result = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = "操作成功"
                            }
                        }
                    }
                };
                await SendJsonAsync(result);
            }
            catch (Exception ex)
            {
                var error = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32603,
                        ["message"] = ex.Message
                    }
                };
                await SendJsonAsync(error);
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
                _logger.LogInformation("📤 发送消息: {Json}", jsonString);

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
                    _serviceStore.UpdateConnectionStatus(_deviceToken, _serviceId, ConnectionId, false);
                    _toolRegistry.RemoveConnection(_deviceToken, _serviceId);
                    _logger.LogInformation("🔌 连接已关闭，服务：{ServiceId}，设备Token: {Token}",
                        _serviceId, _deviceToken);
                }
                else
                {
                    var tempKey = $"binding:{_deviceToken}:pending";
                    _serviceStore.Remove(tempKey);
                    _toolRegistry.UnregisterDeviceTools(_deviceToken);
                    _logger.LogInformation("🔌 临时连接已关闭，设备Token: {Token}", _deviceToken);
                }
            }
        }
    }
}
