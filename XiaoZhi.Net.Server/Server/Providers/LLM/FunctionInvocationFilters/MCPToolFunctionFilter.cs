using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Providers.LLM.FunctionInvocationFilters
{
    internal class MCPToolFunctionFilter : IFunctionInvocationFilter
    {
        private readonly HashSet<string> _subMCPClientTypeNames = new HashSet<string>(3) { SubMCPClientTypeNames.DeviceMcpClient, SubMCPClientTypeNames.McpEndpointClient, SubMCPClientTypeNames.ServerMcpClient };
        private readonly ILogger _logger;

        private const string IOT_COMPONENT_PATTERN = @"^" + SubMCPClientTypeNames.DeviceIoTClient + @"_(.+?)_\d+$";

        private readonly IServiceProvider _serviceProvider;
        public MCPToolFunctionFilter(ILogger<MCPToolFunctionFilter> logger, IServiceProvider serviceProvider)
        {
            this._logger = logger;
            this._serviceProvider = serviceProvider;
        }
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (!string.IsNullOrEmpty(context.Function.PluginName) && context.Kernel.Data.TryGetValue("session", out var data) && data is not null && data is Session session)
            {
                if (session.SessionCtsToken.IsCancellationRequested)
                {
                    this._logger.LogDebug(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_FunctionCancelled, context.Function.Name);
                    throw new OperationCanceledException(session.SessionCtsToken);
                }

                switch (context.Function.PluginName)
                {
                    case "DeviceMcpClient":
                        await HandleDeviceCommandAsync(context, session);
                        break;

                    case "ThirdPartyService":
                        await HandleThirdPartyCommandAsync(context, session);
                        break;

                    default:
                        await next(context);
                        break;
                }
            }
            else
            {
                await next(context);
            }
        }
        /// <summary>
        /// 处理终端指令
        /// </summary>
        private async Task HandleDeviceCommandAsync(FunctionInvocationContext context, Session session)
        {
            try
            {
                // 检查设备是否已绑定
                if (!session.IsDeviceBinded)
                {
                    context.Result = new FunctionResult(context.Result, "设备未绑定，无法执行终端指令");
                    return;
                }

                if (session.PrivateProvider.McpClient is null)
                {
                    this._logger.LogWarning(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_McpClientNotInit, session.DeviceId);
                    // 不在这里调用 next，而是返回 null 或特殊值，让调用方处理
                    context.Result = new FunctionResult(context.Result, "MCP客户端未初始化");
                    return;
                }

                ISubMcpClient? subMcpClient = session.PrivateProvider.McpClient.GetSubMcpClient(context.Function.PluginName);

                if (subMcpClient is null)
                {
                    throw new InvalidOperationException(string.Format(Lang.MCPToolFunctionFilter_OnFunctionInvocationAsync_SubMcpClientNotFound, context.Function.PluginName));
                }

                string callResult = await subMcpClient.CallMcpToolAsync(context.Function.Name, context.Arguments);
                context.Result = new FunctionResult(context.Result, callResult);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "执行终端指令失败: {FunctionName}", context.Function.Name);
                context.Result = new FunctionResult(context.Result, $"终端指令执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理三方指令
        /// </summary>
        private async Task HandleThirdPartyCommandAsync(FunctionInvocationContext context, Session session)
        {
            try
            {
                // 获取 ToolRouter 和 McpToolInvoker
                var toolRouter = GetToolRouter();
                var toolInvoker = GetToolInvoker();

                if (toolRouter == null || toolInvoker == null)
                {
                    throw new InvalidOperationException("无法获取工具路由或调用器");
                }

                // 注意：context.Function.Name 是带占位符的格式（点号被替换为'4'），需要转回点号
                string originalToolName = context.Function.Name.Replace('4', '.');

                // 使用 McpToolInvoker 调用工具
                var result = await toolInvoker.InvokeAsync(originalToolName, context.Arguments, session.SessionCtsToken);

                context.Result = new FunctionResult(context.Result, result);
            }
            catch (TimeoutException ex)
            {
                this._logger.LogWarning(ex, "三方指令执行超时: {FunctionName}", context.Function.Name);
                context.Result = new FunctionResult(context.Result, $"三方指令执行超时: {ex.Message}");
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "执行三方指令失败: {FunctionName}", context.Function.Name);
                context.Result = new FunctionResult(context.Result, $"三方指令执行失败: {ex.Message}");
            }
        }
        /// <summary>
        /// 获取 ToolRouter 实例
        /// </summary>
        private ToolRouter? GetToolRouter()
        {
            try
            {
                return _serviceProvider.GetService<ToolRouter>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取 McpToolInvoker 实例
        /// </summary>
        private McpToolInvoker? GetToolInvoker()
        {
            try
            {
                return _serviceProvider.GetService<McpToolInvoker>();
            }
            catch
            {
                return null;
            }
        }
    }
}
