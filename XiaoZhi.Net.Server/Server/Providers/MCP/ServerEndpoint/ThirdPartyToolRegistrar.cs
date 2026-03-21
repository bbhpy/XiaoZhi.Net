using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 三方工具注册器，负责将三方工具动态注册到对应设备的 Kernel 中
    /// </summary>
    internal class ThirdPartyToolRegistrar
    {
        private const char DOT_PLACEHOLDER = '4';  // 点号占位符
        private const char REAL_DOT = '.';

        private readonly ILogger<ThirdPartyToolRegistrar> _logger;
        private readonly McpServiceStore _serviceStore;
        private readonly TokenSessionRegistry _tokenRegistry;
        private readonly ToolRegistry _toolRegistry;

        // 记录每个设备当前已注册的工具名（用于去重和更新）
        private readonly ConcurrentDictionary<string, HashSet<string>> _deviceRegisteredTools = new();

        public ThirdPartyToolRegistrar(
            ILogger<ThirdPartyToolRegistrar> logger,
            McpServiceStore serviceStore,
            TokenSessionRegistry tokenRegistry,
            ToolRegistry toolRegistry)
        {
            _logger = logger;
            _serviceStore = serviceStore;
            _tokenRegistry = tokenRegistry;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// 当三方服务工具列表更新时调用，将该设备的所有三方工具注册到 Kernel
        /// </summary>
        /// <param name="deviceToken">设备Token</param>
        /// <param name="serviceId">服务ID（可选，用于日志）</param>
        public async Task RegisterToolsForDeviceAsync(string deviceToken, string serviceId)
        {
            try
            {
                _logger.LogInformation("开始为设备 {DeviceToken} 注册三方工具，服务: {ServiceId}", deviceToken, serviceId);

                // 1. 通过 token 获取 session
                var session = _tokenRegistry.GetSession(deviceToken);
                if (session == null)
                {
                    _logger.LogWarning("无法找到设备 {DeviceToken} 的 Session，可能设备未连接", deviceToken);
                    return;
                }

                _logger.LogDebug("设备 {DeviceToken} 的 Session 已找到，SessionId: {SessionId}", deviceToken, session.SessionId);

                // 2. 获取该设备绑定的所有三方工具
                var allTools = _serviceStore.GetAllToolsByDevice(deviceToken);
                if (allTools == null || !allTools.Any())
                {
                    _logger.LogDebug("设备 {DeviceToken} 没有绑定的三方工具", deviceToken);
                    return;
                }

                _logger.LogInformation("设备 {DeviceToken} 共有 {Count} 个三方工具待注册", deviceToken, allTools.Count);

                // 3. 获取设备的 Kernel
                var kernel = session.PrivateProvider?.Kernel;
                if (kernel == null)
                {
                    _logger.LogWarning("设备 {DeviceToken} 的 Kernel 未初始化", deviceToken);
                    return;
                }

                // 4. 构建 KernelFunction 列表
                var functions = new List<KernelFunction>();
                var toolNames = new HashSet<string>();

                foreach (var tool in allTools)
                {
                    // 工具名转换：点号替换为占位符
                    string functionName = tool.Name.Replace(REAL_DOT, DOT_PLACEHOLDER);
                    toolNames.Add(functionName);

                    // 解析参数
                    var parameters = new List<KernelParameterMetadata>();
                    if (tool.InputSchema != null &&
                        tool.InputSchema.TryGetPropertyValue("properties", out var propsNode) &&
                        propsNode is JsonObject properties)
                    {
                        foreach (var prop in properties)
                        {
                            if (prop.Value is JsonObject propObj)
                            {
                                var param = new KernelParameterMetadata(prop.Key)
                                {
                                    Description = propObj["description"]?.GetValue<string>() ?? string.Empty,
                                    IsRequired = false
                                };

                                // 检查是否为必填参数
                                if (tool.InputSchema.TryGetPropertyValue("required", out var requiredNode) &&
                                    requiredNode is JsonArray requiredArray)
                                {
                                    param.IsRequired = requiredArray.Any(x => x?.GetValue<string>() == prop.Key);
                                }

                                // 判断参数类型
                                var type = propObj["type"]?.GetValue<string>();
                                if (type == "number" || type == "integer")
                                    param.ParameterType = typeof(int);
                                else
                                    param.ParameterType = typeof(string);

                                parameters.Add(param);
                            }
                        }
                    }

                    // 创建 KernelFunction - 捕获 tool 变量
                    var capturedTool = tool;
                    var function = KernelFunctionFactory.CreateFromMethod(
                        async (KernelArguments args) =>
                        {
                            _logger.LogDebug("调用三方工具: {ToolName}, 参数: {Args}", capturedTool.Name, args.ToJson());

                            var result = await _toolRegistry.CallThirdPartyToolAsync(capturedTool.Name, args);

                            // 安全地解析结果
                            if (result.TryGetPropertyValue("content", out var contentNode) &&
                                contentNode is JsonArray contentArray &&
                                contentArray.Count > 0 &&
                                contentArray[0] is JsonObject first &&
                                first.TryGetPropertyValue("text", out var textNode) &&
                                textNode != null)
                            {
                                return textNode.GetValue<string>();
                            }

                            return result.ToJsonString();
                        },
                        functionName,
                        tool.Description,
                        parameters);

                    functions.Add(function);
                    _logger.LogDebug("准备注册三方工具: {OriginalName} -> {FunctionName}", tool.Name, functionName);
                }

                // 5. 移除旧的 ThirdPartyService 插件（如果存在）
                if (kernel.Plugins.TryGetPlugin("ThirdPartyService", out var oldPlugin))
                {
                    _logger.LogDebug("移除旧的 ThirdPartyService 插件，原包含 {Count} 个工具",
                        oldPlugin.Count());
                    kernel.Plugins.Remove(oldPlugin);
                }

                // 6. 注册新的 ThirdPartyService 插件
                if (functions.Any())
                {
                    kernel.ImportPluginFromFunctions("ThirdPartyService", functions);

                    // 记录已注册的工具
                    _deviceRegisteredTools[deviceToken] = toolNames;

                    _logger.LogInformation("✅ 设备 {DeviceToken} 已注册 {Count} 个三方工具: {Tools}",
                        deviceToken, functions.Count, string.Join(", ", toolNames));
                }
                else
                {
                    _logger.LogDebug("设备 {DeviceToken} 没有需要注册的三方工具", deviceToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备 {DeviceToken} 注册三方工具失败", deviceToken);
            }
        }

        /// <summary>
        /// 当三方服务断开时，从 Kernel 中移除该服务的工具
        /// </summary>
        /// <param name="deviceToken">设备Token</param>
        /// <param name="serviceId">服务ID（可选，用于日志）</param>
        public async Task UnregisterToolsForDeviceAsync(string deviceToken, string serviceId)
        {
            try
            {
                _logger.LogInformation("开始为设备 {DeviceToken} 移除三方工具，服务: {ServiceId}", deviceToken, serviceId);

                // 获取该设备剩余的三方工具
                var remainingTools = _serviceStore.GetAllToolsByDevice(deviceToken);

                var session = _tokenRegistry.GetSession(deviceToken);
                if (session == null)
                {
                    _logger.LogDebug("设备 {DeviceToken} 的 Session 不存在，跳过清理", deviceToken);
                    return;
                }

                var kernel = session.PrivateProvider?.Kernel;
                if (kernel == null) return;

                // 如果没有剩余工具，直接移除整个插件
                if (remainingTools == null || !remainingTools.Any())
                {
                    if (kernel.Plugins.TryGetPlugin("ThirdPartyService", out var plugin))
                    {
                        kernel.Plugins.Remove(plugin);
                        _deviceRegisteredTools.TryRemove(deviceToken, out _);
                        _logger.LogInformation("设备 {DeviceToken} 的所有三方工具已移除", deviceToken);
                    }
                    return;
                }

                // 有剩余工具，重新注册（更新插件）
                await RegisterToolsForDeviceAsync(deviceToken, serviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备 {DeviceToken} 移除三方工具失败", deviceToken);
            }
        }

        /// <summary>
        /// 刷新指定设备的所有三方工具（当工具列表发生变化时调用）
        /// </summary>
        public async Task RefreshDeviceToolsAsync(string deviceToken)
        {
            await RegisterToolsForDeviceAsync(deviceToken, string.Empty);
        }
    }
}