using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Server.Providers.MCP.Events;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// 三方工具注册器
    /// 订阅事件，负责将三方工具注册到对应设备的 Kernel 中
    /// </summary>
    internal class ThirdPartyToolRegistrar : IDisposable
    {
        private const char DOT_PLACEHOLDER = '4';  // 点号占位符
        private const char REAL_DOT = '.';

        private readonly ILogger<ThirdPartyToolRegistrar> _logger;
        private readonly McpServiceStore _serviceStore;
        private readonly ToolRouter _toolRouter;
        private readonly TokenSessionRegistry _tokenRegistry;
        private readonly IEventPublisher _eventPublisher;
        private readonly McpToolInvoker _toolInvoker;

        // 记录每个设备当前已注册的工具名（用于去重和更新）
        private readonly ConcurrentDictionary<string, HashSet<string>> _deviceRegisteredTools = new();

        // 事件订阅的取消令牌
        private readonly List<IDisposable> _subscriptions = new();

        public ThirdPartyToolRegistrar(
            ILogger<ThirdPartyToolRegistrar> logger,
            McpServiceStore serviceStore,
            ToolRouter toolRouter,
            TokenSessionRegistry tokenRegistry,
            IEventPublisher eventPublisher,
            McpToolInvoker toolInvoker)
        {
            _logger = logger;
            _serviceStore = serviceStore;
            _toolRouter = toolRouter;
            _tokenRegistry = tokenRegistry;
            _eventPublisher = eventPublisher;
            _toolInvoker = toolInvoker;

            _logger.LogInformation("===== ThirdPartyToolRegistrar 构造函数被调用 =====");
            // 订阅事件
            _subscriptions.Add(_eventPublisher.Subscribe<ServiceBoundEvent>(OnServiceBound));
            _subscriptions.Add(_eventPublisher.Subscribe<DeviceOnlineEvent>(OnDeviceOnline));
            _subscriptions.Add(_eventPublisher.Subscribe<ServiceUnboundEvent>(OnServiceUnbound));

            _logger.LogInformation("ThirdPartyToolRegistrar 已启动，已订阅 {Count} 个事件", _subscriptions.Count);
        }

        /// <summary>
        /// 处理服务绑定事件（三方服务连接并发送工具列表后触发）
        /// </summary>
        private async void OnServiceBound(ServiceBoundEvent @event)
        {
            try
            {
                _logger.LogInformation("收到服务绑定事件: 设备 {DeviceToken}, 服务 {ServiceId}",
                    @event.DeviceToken, @event.ServiceId);

                // 检查设备是否在线
                var session = _tokenRegistry.GetSession(@event.DeviceToken);
                if (session == null)
                {
                    _logger.LogDebug("设备 {DeviceToken} 不在线，跳过立即注册，等待设备上线",
                        @event.DeviceToken);
                    return;
                }

                // 设备在线，立即注册该服务
                await RegisterServiceForDeviceAsync(@event.DeviceToken, @event.ServiceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理服务绑定事件失败: 设备 {DeviceToken}, 服务 {ServiceId}",
                    @event.DeviceToken, @event.ServiceId);
            }
        }

        /// <summary>
        /// 处理设备上线事件
        /// </summary>
        private async void OnDeviceOnline(DeviceOnlineEvent @event)
        {
            try
            {
                _logger.LogInformation("===== OnDeviceOnline 被触发 =====");
                _logger.LogInformation("设备 Token: {DeviceToken}, 会话: {SessionId}, 时间: {OnlineAt}",
                    @event.DeviceToken, @event.SessionId, @event.OnlineAt);

                // 检查存储中的绑定
                var bindings = _serviceStore.GetBindingsByDevice(@event.DeviceToken);
                _logger.LogInformation("设备 {DeviceToken} 在存储中有 {Count} 个绑定",
                    @event.DeviceToken, bindings.Count);

                foreach (var binding in bindings)
                {
                    _logger.LogInformation("  - 服务: {ServiceId}, 工具数: {ToolCount}, 最后更新: {LastUpdated}",
                        binding.ServiceId, binding.Tools?.Count ?? 0, binding.LastUpdatedAt);
                }

                // 设备上线，注册所有绑定的三方工具
                await RegisterToolsForDeviceAsync(@event.DeviceToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理设备上线事件失败: 设备 {DeviceToken}", @event.DeviceToken);
            }
        }

        /// <summary>
        /// 处理服务解绑事件（三方服务断开连接时触发）
        /// </summary>
        private async void OnServiceUnbound(ServiceUnboundEvent @event)
        {
            try
            {
                _logger.LogInformation("收到服务解绑事件: 设备 {DeviceToken}, 服务 {ServiceId}",
                    @event.DeviceToken, @event.ServiceId);

                // 检查设备是否在线
                var session = _tokenRegistry.GetSession(@event.DeviceToken);
                if (session == null)
                {
                    _logger.LogDebug("设备 {DeviceToken} 不在线，跳过清理", @event.DeviceToken);
                    return;
                }

                // 设备在线，移除该服务的工具并重新注册剩余工具
                await UnregisterServiceForDeviceAsync(@event.DeviceToken, @event.ServiceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理服务解绑事件失败: 设备 {DeviceToken}, 服务 {ServiceId}",
                    @event.DeviceToken, @event.ServiceId);
            }
        }

        /// <summary>
        /// 为设备注册所有三方工具（设备上线时调用）
        /// </summary>
        public async Task RegisterToolsForDeviceAsync(string deviceToken)
        {
            try
            {
                _logger.LogInformation("开始为设备 {DeviceToken} 注册三方工具", deviceToken);

                // 获取该设备绑定的所有三方工具
                var allTools = _serviceStore.GetAllToolsByDevice(deviceToken);
                if (allTools == null || !allTools.Any())
                {
                    _logger.LogDebug("设备 {DeviceToken} 没有绑定的三方工具", deviceToken);
                    return;
                }

                _logger.LogInformation("设备 {DeviceToken} 共有 {Count} 个三方工具待注册",
                    deviceToken, allTools.Count);

                // 按服务分组注册
                var bindings = _serviceStore.GetBindingsByDevice(deviceToken);
                foreach (var binding in bindings)
                {
                    if (binding.Tools != null && binding.Tools.Any())
                    {
                        await RegisterServiceInternalAsync(deviceToken, binding.ServiceId, binding.Tools);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备 {DeviceToken} 注册三方工具失败", deviceToken);
            }
        }

        /// <summary>
        /// 为设备注册单个服务（动态绑定时调用）
        /// </summary>
        public async Task RegisterServiceForDeviceAsync(string deviceToken, string serviceId)
        {
            try
            {
                var binding = _serviceStore.GetBinding(deviceToken, serviceId);
                if (binding == null || binding.Tools == null || !binding.Tools.Any())
                {
                    _logger.LogWarning("设备 {DeviceToken} 服务 {ServiceId} 没有工具定义",
                        deviceToken, serviceId);
                    return;
                }

                await RegisterServiceInternalAsync(deviceToken, serviceId, binding.Tools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备 {DeviceToken} 服务 {ServiceId} 注册工具失败",
                    deviceToken, serviceId);
            }
        }

        /// <summary>
        /// 内部注册方法
        /// </summary>
        private async Task RegisterServiceInternalAsync(string deviceToken, string serviceId, List<ToolDefinition> tools)
        {
            var session = _tokenRegistry.GetSession(deviceToken);
            if (session == null)
            {
                _logger.LogWarning("设备 {DeviceToken} 的 Session 不存在，跳过注册", deviceToken);
                return;
            }

            var kernel = session.PrivateProvider?.Kernel;
            if (kernel == null)
            {
                _logger.LogWarning("设备 {DeviceToken} 的 Kernel 未初始化", deviceToken);
                return;
            }

            // 构建 KernelFunction 列表
            var functions = new List<KernelFunction>();
            var toolNames = new HashSet<string>();

            foreach (var tool in tools)
            {
                // 工具名转换：点号替换为占位符
                string functionName = tool.Name.Replace(REAL_DOT, DOT_PLACEHOLDER);
                toolNames.Add(functionName);

                // 解析参数
                var parameters = ParseParameters(tool);

                // 创建 KernelFunction - 捕获变量（注意：tool.Name 在闭包中使用）
                var capturedToolName = tool.Name;
                var function = KernelFunctionFactory.CreateFromMethod(
                    async (KernelArguments args, CancellationToken cancellationToken) =>
                    {
                        _logger.LogDebug("调用三方工具: {ToolName}, 参数: {Args}",
                            capturedToolName, args.ToJson());

                        // 实际调用三方服务
                        return await _toolInvoker.InvokeAsync(capturedToolName, args, cancellationToken);
                    },
                    functionName,
                    tool.Description,
                    parameters);

                functions.Add(function);
                _logger.LogDebug("准备注册三方工具: {OriginalName} -> {FunctionName}",
                    tool.Name, functionName);
            }

            // 获取或创建插件
            const string pluginName = "ThirdPartyService";

            // 移除旧的插件（如果存在）
            if (kernel.Plugins.TryGetPlugin(pluginName, out var oldPlugin))
            {
                kernel.Plugins.Remove(oldPlugin);
                _logger.LogDebug("移除旧的 {PluginName} 插件", pluginName);
            }

            // 注册新的插件
            if (functions.Any())
            {
                kernel.ImportPluginFromFunctions(pluginName, functions);

                // 记录已注册的工具
                var existingTools = _deviceRegisteredTools.GetOrAdd(deviceToken, _ => new HashSet<string>());
                foreach (var fnName in toolNames)
                {
                    existingTools.Add(fnName);
                }

                // 注册路由到 ToolRouter
                foreach (var tool in tools)
                {
                    _toolRouter.RegisterRoute(tool.Name, deviceToken, serviceId);
                }

                _logger.LogInformation("设备 {DeviceToken} 服务 {ServiceId} 已注册 {Count} 个三方工具: {Tools}",
                    deviceToken, serviceId, functions.Count, string.Join(", ", toolNames));
            }
        }

        /// <summary>
        /// 解析工具参数元数据
        /// </summary>
        private List<KernelParameterMetadata> ParseParameters(ToolDefinition tool)
        {
            var parameters = new List<KernelParameterMetadata>();

            if (tool.InputSchema != null &&
                tool.InputSchema.TryGetPropertyValue("properties", out var propsNode) &&
                propsNode is JsonObject properties)
            {
                // 获取必填参数列表
                var requiredParams = new HashSet<string>();
                if (tool.InputSchema.TryGetPropertyValue("required", out var requiredNode) &&
                    requiredNode is JsonArray requiredArray)
                {
                    foreach (var item in requiredArray)
                    {
                        if (item?.GetValue<string>() is string reqName)
                            requiredParams.Add(reqName);
                    }
                }

                foreach (var prop in properties)
                {
                    if (prop.Value is JsonObject propObj)
                    {
                        var param = new KernelParameterMetadata(prop.Key)
                        {
                            Description = propObj["description"]?.GetValue<string>() ?? string.Empty,
                            IsRequired = requiredParams.Contains(prop.Key)
                        };

                        // 判断参数类型
                        var type = propObj["type"]?.GetValue<string>();
                        param.ParameterType = type switch
                        {
                            "number" or "integer" => typeof(int),
                            "boolean" => typeof(bool),
                            "array" => typeof(string[]),
                            _ => typeof(string)
                        };

                        parameters.Add(param);
                    }
                }
            }

            return parameters;
        }

        /// <summary>
        /// 移除设备的指定服务工具
        /// </summary>
        public async Task UnregisterServiceForDeviceAsync(string deviceToken, string serviceId)
        {
            try
            {
                _logger.LogInformation("开始为设备 {DeviceToken} 移除服务 {ServiceId} 的工具",
                    deviceToken, serviceId);

                // 移除路由
                _toolRouter.UnregisterServiceRoutes(deviceToken, serviceId);

                // 获取该设备剩余的三方工具
                var remainingBindings = _serviceStore.GetBindingsByDevice(deviceToken)
                    .Where(b => b.ServiceId != serviceId)
                    .ToList();

                var session = _tokenRegistry.GetSession(deviceToken);
                if (session == null)
                {
                    _logger.LogDebug("设备 {DeviceToken} 的 Session 不存在，跳过清理", deviceToken);
                    return;
                }

                var kernel = session.PrivateProvider?.Kernel;
                if (kernel == null) return;

                const string pluginName = "ThirdPartyService";

                // 如果没有剩余工具，直接移除整个插件
                if (!remainingBindings.Any() || remainingBindings.All(b => b.Tools == null || !b.Tools.Any()))
                {
                    if (kernel.Plugins.TryGetPlugin(pluginName, out var plugin))
                    {
                        kernel.Plugins.Remove(plugin);
                        _deviceRegisteredTools.TryRemove(deviceToken, out _);
                        _logger.LogInformation("设备 {DeviceToken} 的所有三方工具已移除", deviceToken);
                    }
                    return;
                }

                // 有剩余工具，重新注册（更新插件）
                await RegisterRemainingToolsAsync(deviceToken, remainingBindings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为设备 {DeviceToken} 移除服务 {ServiceId} 工具失败",
                    deviceToken, serviceId);
            }
        }

        /// <summary>
        /// 为设备注册剩余工具（实际调用版本）
        /// </summary>
        private async Task RegisterRemainingToolsAsync(string deviceToken, List<ServiceBinding> remainingBindings)
        {
            var session = _tokenRegistry.GetSession(deviceToken);
            if (session == null) return;

            var kernel = session.PrivateProvider?.Kernel;
            if (kernel == null) return;

            var allFunctions = new List<KernelFunction>();
            var allToolNames = new HashSet<string>();

            foreach (var binding in remainingBindings)
            {
                if (binding.Tools == null) continue;

                foreach (var tool in binding.Tools)
                {
                    string functionName = tool.Name.Replace(REAL_DOT, DOT_PLACEHOLDER);
                    allToolNames.Add(functionName);

                    // 解析参数
                    var parameters = ParseParameters(tool);

                    // 创建 KernelFunction - 实际调用
                    var capturedToolName = tool.Name;
                    var function = KernelFunctionFactory.CreateFromMethod(
                        async (KernelArguments args, CancellationToken cancellationToken) =>
                        {
                            _logger.LogDebug("调用三方工具: {ToolName}, 参数: {Args}",
                                capturedToolName, args.ToJson());

                            return await _toolInvoker.InvokeAsync(capturedToolName, args, cancellationToken);
                        },
                        functionName,
                        tool.Description,
                        parameters);

                    allFunctions.Add(function);
                }
            }

            const string pluginName = "ThirdPartyService";
            if (kernel.Plugins.TryGetPlugin(pluginName, out var oldPlugin))
            {
                kernel.Plugins.Remove(oldPlugin);
            }

            if (allFunctions.Any())
            {
                kernel.ImportPluginFromFunctions(pluginName, allFunctions);
                _deviceRegisteredTools[deviceToken] = allToolNames;
                _logger.LogInformation("设备 {DeviceToken} 已重新注册 {Count} 个剩余三方工具",
                    deviceToken, allFunctions.Count);
            }
        }

        /// <summary>
        /// 刷新指定设备的所有三方工具
        /// </summary>
        public async Task RefreshDeviceToolsAsync(string deviceToken)
        {
            await RegisterToolsForDeviceAsync(deviceToken);
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription?.Dispose();
            }
            _subscriptions.Clear();
            _logger.LogInformation("ThirdPartyToolRegistrar 已释放");
        }
    }
}