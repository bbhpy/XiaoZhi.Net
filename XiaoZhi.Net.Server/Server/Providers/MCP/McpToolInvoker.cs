using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Server.Providers.MCP
{
    internal class McpToolInvoker
    {
        private readonly ToolRouter _toolRouter;
        private readonly McpConnectionManager _connectionManager;
        private readonly McpCallManager _callManager;
        private readonly ILogger<McpToolInvoker> _logger;

        public McpToolInvoker(
            ToolRouter toolRouter,
            McpConnectionManager connectionManager,
            McpCallManager callManager,
            ILogger<McpToolInvoker> logger)
        {
            _toolRouter = toolRouter;
            _connectionManager = connectionManager;
            _callManager = callManager;
            _logger = logger;
        }

        public async Task<string> InvokeAsync(string toolName, KernelArguments arguments, CancellationToken cancellationToken = default)
        {
            // 1. 查找目标
            var targets = _toolRouter.FindTargets(toolName);
            if (!targets.Any())
                throw new Exception($"工具 {toolName} 未找到");

            var (deviceToken, serviceId) = targets.First();

            _logger.LogDebug("调用工具 {ToolName}: 目标设备 {DeviceToken}, 服务 {ServiceId}",
                toolName, deviceToken, serviceId);

            // 2. 获取连接
            var socket = _connectionManager.GetConnection(deviceToken, serviceId);
            if (socket == null)
                throw new Exception($"工具 {toolName} 的服务连接已断开");

            // 3. 生成调用ID
            var callId = _callManager.NextCallId;

            // 4. 注册等待
            var task = _callManager.RegisterPendingCall(callId);

            try
            {
                // 5. 构建请求参数 - 改进参数类型转换
                var argsObject = new JsonObject();
                foreach (var arg in arguments)
                {
                    if (arg.Value == null)
                        continue;

                    // 尝试智能转换参数类型
                    var value = ConvertArgumentValue(arg.Value);
                    argsObject[arg.Key] = value;
                }

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

                // 6. 发送请求
                var json = request.ToJsonString();
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

                _logger.LogDebug("已发送工具调用请求: {ToolName}, CallId: {CallId}", toolName, callId);

                // 7. 等待响应（带超时）
                var timeout = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                var completed = await Task.WhenAny(task, timeout);

                if (completed == timeout)
                {
                    _callManager.CleanPendingCall(callId);
                    throw new TimeoutException($"调用工具 {toolName} 超时");
                }

                var result = await task;

                // 8. 解析结果
                return ParseResult(result, toolName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调用工具 {ToolName} 失败", toolName);
                _callManager.CleanPendingCall(callId);
                throw;
            }
        }

        /// <summary>
        /// 智能转换参数值
        /// </summary>
        private JsonNode? ConvertArgumentValue(object value)
        {
            return value switch
            {
                int intValue => JsonValue.Create(intValue),
                long longValue => JsonValue.Create(longValue),
                double doubleValue => JsonValue.Create(doubleValue),
                bool boolValue => JsonValue.Create(boolValue),
                string strValue => TryParseNumericString(strValue),
                JsonNode jsonNode => jsonNode,
                _ => JsonValue.Create(value.ToString())
            };
        }

        /// <summary>
        /// 尝试将字符串解析为数字
        /// </summary>
        private JsonNode? TryParseNumericString(string value)
        {
            if (int.TryParse(value, out int intValue))
                return JsonValue.Create(intValue);
            if (long.TryParse(value, out long longValue))
                return JsonValue.Create(longValue);
            if (double.TryParse(value, out double doubleValue))
                return JsonValue.Create(doubleValue);
            if (bool.TryParse(value, out bool boolValue))
                return JsonValue.Create(boolValue);
            return JsonValue.Create(value);
        }

        /// <summary>
        /// 解析 MCP 响应结果
        /// </summary>
        private string ParseResult(JsonObject result, string toolName)
        {
            // 检查错误
            if (result.TryGetPropertyValue("error", out var errorNode) && errorNode is JsonObject errorObj)
            {
                var errorCode = errorObj["code"]?.GetValue<int>() ?? -1;
                var errorMessage = errorObj["message"]?.GetValue<string>() ?? "未知错误";
                throw new Exception($"工具 {toolName} 调用失败: [{errorCode}] {errorMessage}");
            }

            // 解析 result 结构
            if (result.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonObject resultObj)
            {
                // 标准 MCP 工具响应格式: { content: [{ type: "text", text: "..." }] }
                if (resultObj.TryGetPropertyValue("content", out var contentNode) && contentNode is JsonArray contentArray && contentArray.Count > 0)
                {
                    var first = contentArray[0] as JsonObject;
                    if (first != null)
                    {
                        // 文本类型
                        if (first.TryGetPropertyValue("text", out var textNode))
                            return textNode.GetValue<string>();

                        // 其他类型，返回完整内容
                        return first.ToJsonString();
                    }
                }

                // 没有 content 数组，返回整个 result
                return resultObj.ToJsonString();
            }

            // 没有 result 字段，返回完整响应
            return result.ToJsonString();
        }
    }
}