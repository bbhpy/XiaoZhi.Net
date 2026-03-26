using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
    /// <summary>
    /// 服务器MCP客户端类，继承自BaseMcpClient并实现ISubMcpClient接口   当前设计只支持连接一个MCP服务器，后续如果需要支持多个，可以改为维护一个MCP客户端列表，并在调用时指定使用哪个客户端
    /// </summary>
    internal class ServerMcpClient : BaseProvider<ServerMcpClient, MCPClientBuildConfig>, ISubMcpClient
    {
        private bool _isInitialized = false;
        private const char DOT_PLACEHOLDER = '4';
        private const char REAL_DOT = '.';

        private readonly ToolRouter _toolRouter;
        private readonly McpServiceStore _serviceStore;
        private readonly McpToolInvoker _toolInvoker;

        public ServerMcpClient(
            ILogger<ServerMcpClient> logger,
            ToolRouter toolRouter,
            McpServiceStore serviceStore,
            McpToolInvoker toolInvoker)
            : base(logger)
        {
            _toolRouter = toolRouter;
            _serviceStore = serviceStore;
            _toolInvoker = toolInvoker;
        }

        public override string ModelName => SubMCPClientTypeNames.ServerMcpClient;
        public override string ProviderType => "SubMcpClient";

        public ICollection<KernelFunction> Functions { get; private set; } = new List<KernelFunction>();
        public bool IsReady => _isInitialized;
        public int NextId => throw new NotImplementedException();

        public override bool Build(MCPClientBuildConfig config)
        {
            try
            {
                var session = config.Session;
                var modelSetting = config.ModelSetting;

                if (this.ModelName.ToLower() == "stdio-client")
                {
                    string? name = modelSetting.Config.GetConfigValueOrDefault("Name");
                    string command = modelSetting.Config.GetConfigValueOrDefault("Command", string.Empty);
                    List<string> arguments = modelSetting.Config.GetConfigValueOrDefault("Arguments", new List<string>());

                    var kernel = session.PrivateProvider?.Kernel;
                    if (kernel == null)
                    {
                        this.Logger.LogError("Kernel is null, cannot register MCP tools");
                        return false;
                    }

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            this.Logger.LogInformation($"Connecting to MCP server: {command} {string.Join(" ", arguments)}");

                            // 1. 创建传输
                            var transport = new StdioClientTransport(new StdioClientTransportOptions
                            {
                                Name = name ?? "MCP Server",
                                Command = command,
                                Arguments = arguments.ToArray()
                            });

                            // 2. 创建客户端选项
                            var clientOptions = new McpClientOptions
                            {
                                ClientInfo = new Implementation
                                {
                                    Name = "XiaoZhi.Net.Server",
                                    Version = "1.0.0"
                                }
                            };

                            // 3. 创建 MCP 客户端
                            var mcpClient = await ModelContextProtocol.Client.McpClient.CreateAsync(
                                transport,
                                clientOptions,
                                loggerFactory: null,
                                cancellationToken: default);

                            // 4. 获取工具列表
                            var tools = await mcpClient.ListToolsAsync(cancellationToken: default);
                            this.Logger.LogInformation($"Retrieved {tools.Count} tools from MCP server");

                            // 5. 转换为 KernelFunction
                            var functions = new List<KernelFunction>();
                            foreach (var tool in tools)
                            {
                                var function = ConvertMcpToolToKernelFunction(tool);
                                functions.Add(function);
                                this.Logger.LogDebug($"Registered MCP tool: {tool.Name}");
                            }

                            // 6. 注册到 Kernel
                            if (functions.Any())
                            {
                                const string pluginName = "ThirdPartyService";
                                if (kernel.Plugins.TryGetPlugin(pluginName, out var oldPlugin))
                                {
                                    kernel.Plugins.Remove(oldPlugin);
                                }

                                kernel.Plugins.AddFromFunctions(pluginName, functions);
                                this.Logger.LogInformation($"Registered {functions.Count} MCP tools to kernel");

                                this.Functions = functions;
                            }

                            _isInitialized = true;
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogError(ex, "Failed to initialize MCP client");
                        }
                    });

                    if (!task.Wait(TimeSpan.FromSeconds(30)))
                    {
                        this.Logger.LogError("MCP client initialization timeout");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build ServerMcpClient");
                return false;
            }
        }

        private KernelFunction ConvertMcpToolToKernelFunction(ModelContextProtocol.Client.McpClientTool tool)
        {
            var parameters = new List<KernelParameterMetadata>();
            var protocolTool = tool.ProtocolTool;

            if (protocolTool.InputSchema.ValueKind != JsonValueKind.Undefined)
            {
                var inputSchema = protocolTool.InputSchema;

                // 解析 required 数组
                var requiredParams = new HashSet<string>();
                if (inputSchema.TryGetProperty("required", out var requiredElement) &&
                    requiredElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in requiredElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            requiredParams.Add(item.GetString()!);
                        }
                    }
                }

                // 解析 properties 对象
                if (inputSchema.TryGetProperty("properties", out var propertiesElement) &&
                    propertiesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in propertiesElement.EnumerateObject())
                    {
                        var propName = property.Name;
                        var propValue = property.Value;

                        // 获取 description
                        string description = string.Empty;
                        if (propValue.TryGetProperty("description", out var descElement) &&
                            descElement.ValueKind == JsonValueKind.String)
                        {
                            description = descElement.GetString() ?? string.Empty;
                        }

                        // 获取 type
                        string? type = null;
                        if (propValue.TryGetProperty("type", out var typeElement) &&
                            typeElement.ValueKind == JsonValueKind.String)
                        {
                            type = typeElement.GetString();
                        }

                        var param = new KernelParameterMetadata(propName)
                        {
                            Description = description,
                            IsRequired = requiredParams.Contains(propName)
                        };

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

            return KernelFunctionFactory.CreateFromMethod(
                async (KernelArguments args, CancellationToken cancellationToken) =>
                {
                    this.Logger.LogDebug("Calling MCP tool: {ToolName}, Args: {Args}",
                        tool.Name, args.ToJson());
                    return await _toolInvoker.InvokeAsync(tool.Name, args, cancellationToken);
                },
                SanitizeToolName(tool.Name),
                tool.Description ?? string.Empty,
                parameters
            );
        }

        private string SanitizeToolName(string name)
        {
            string processed = name.Replace(REAL_DOT, DOT_PLACEHOLDER);
            return System.Text.RegularExpressions.Regex.Replace(processed, @"[^a-zA-Z0-9_\\-\u4e00-\u9fff]", "_");
        }

        public override void Dispose()
        {
            _isInitialized = false;
            GC.SuppressFinalize(this);
        }

        // 实现 ISubMcpClient 接口的其他方法
        public Task HandleMcpMessageAsync(JsonObject jsonObject) => Task.CompletedTask;
        public Task SendMcpInitializeAsync() => Task.CompletedTask;
        public Task SendMcpNotificationAsync(string method) => Task.CompletedTask;
        public Task RequestToolsListAsync() => Task.CompletedTask;
        public Task RequestToolsListAsync(string cursor) => Task.CompletedTask;
        public bool HasTool(string toolName) => false;
        public Task<string> CallMcpToolAsync(string toolName, KernelArguments arguments, int timeout = 30)
        {
            throw new NotImplementedException();
        }
    }
}