using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Server.Common.Constants;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    /// <summary>
    /// MCP客户端基类，用于处理MCP协议通信和工具管理
    /// </summary>
    /// <typeparam name="TLogger">日志记录器类型</typeparam>
    internal abstract class BaseMcpClient<TLogger> : BaseProvider<TLogger, MCPClientBuildConfig>, ISubMcpClient
    {
        private const char DOT_PLACEHOLDER = '4';  // U+00B7 间隔号
        private const char REAL_DOT = '.';
        /// <summary>
        /// 用于控制并发访问的信号量，限制同时只能有一个线程访问共享资源
        /// </summary>
        private readonly SemaphoreSlim _lockerSlim = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 空操作委托，用于占位符或默认值
        /// </summary>
        private readonly Action _tempMethod = () => { };

        /// <summary>
        /// 标识对象是否已准备就绪的状态标志
        /// </summary>
        private bool _isReady = false;

        /// <summary>
        /// 用于生成唯一标识符的递增计数器，从1开始分配
        /// </summary>
        private int _nextId = 1;

        /// <summary>
        /// 存储MCP工具的字典，键为工具名称，值为对应的内核函数，使用线程安全的并发字典
        /// </summary>
        private IDictionary<string, KernelFunction> _mcpTools = new ConcurrentDictionary<string, KernelFunction>();

        /// <summary>
        /// 存储调用结果的任务完成源字典，键为调用ID，值为对应的结果任务完成源，使用线程安全的并发字典
        /// </summary>
        private IDictionary<int, TaskCompletionSource<JsonObject>> _callResults = new ConcurrentDictionary<int, TaskCompletionSource<JsonObject>>();

        private readonly ToolRegistry _toolRegistry;
        /// <summary>
        /// 初始化BaseMcpClient实例
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public BaseMcpClient(ILogger<TLogger> logger, ToolRegistry toolRegistry) : base(logger)
        {
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// 获取已注册的函数集合
        /// </summary>
        public ICollection<KernelFunction> Functions => this._mcpTools.Values;

        /// <summary>
        /// 获取或设置客户端是否就绪状态
        /// </summary>
        public bool IsReady
        {
            get
            {
                try
                {
                    this._lockerSlim.Wait();
                    return this._isReady;
                }
                finally
                {
                    this._lockerSlim.Release();
                }
            }
            set
            {
                try
                {
                    this._lockerSlim.Wait();
                    this._isReady = value;
                }
                finally
                {
                    this._lockerSlim.Release();
                }
            }
        }

        /// <summary>
        /// 获取下一个ID（线程安全递增）
        /// </summary>
        public int NextId => Interlocked.Increment(ref this._nextId);

        /// <summary>
        /// 当前会话
        /// </summary>
        protected Session CurrentSession { get; set; } = null!;

        /// <summary>
        /// 额外元数据字典
        /// </summary>
        protected IDictionary<string, object?> AdditionalMetadataDic { get; set; } = null!;

        /// <summary>
        /// 处理MCP消息异步方法
        /// </summary>
        /// <param name="payloadObj">消息负载对象</param>
        /// <returns>异步任务</returns>
        public async virtual Task HandleMcpMessageAsync(JsonObject payloadObj)
        {
            if (this.CurrentSession.PrivateProvider.Kernel is null)
            {
                this.Logger.LogError(Lang.BaseMcpClient_HandleMcpMessageAsync_KernelNotReady, this.CurrentSession.DeviceId);
                return;
            }
            if (payloadObj.TryGetPropertyValue("result", out var result) && result is not null)
            {
                int msgId = payloadObj["id"]?.AsValue().GetValue<int>() ?? 0;

                if (this._callResults.ContainsKey(msgId))
                {
                    this.Logger.LogDebug(Lang.BaseMcpClient_HandleMcpMessageAsync_CallResult, result.ToJsonString(), msgId, this.CurrentSession.DeviceId);
                    this.ResolveCallResult(msgId, result.AsObject());
                    return;
                }

                if (msgId == 1)
                {
                    // mcp initialize id
                    this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_InitMessage, this.CurrentSession.DeviceId);

                    // 解析服务器信息并记录日志
                    if (result.AsObject().TryGetPropertyValue("serverInfo", out var serverInfo) && serverInfo is not null)
                    {
                        string? name = serverInfo["name"]?.GetValue<string>();
                        string? version = serverInfo["version"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                        {
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ServerInfo, name, version);
                        }
                        else
                        {
                            this.Logger.LogWarning(Lang.BaseMcpClient_HandleMcpMessageAsync_InvalidServerInfo);
                        }
                    }

                    return;
                }
                else if (msgId == 2)
                {
                    // mcp tools list id
                    this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ToolListMessage, this.CurrentSession.DeviceId);

                    if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsJson)
                    {
                        // 遍历工具列表并创建KernelFunction
                        foreach (JsonNode? item in toolsJson)
                        {
                            if (item is not JsonObject itemObj)
                                continue;

                            string toolName = item["name"]?.GetValue<string>() ?? "";
                            string toolDescription = item["description"]?.GetValue<string>() ?? "";

                            if (!StaticDeputy.IsValidToolName(toolName))
                            {
                                this.Logger.LogWarning("工具名称 {ToolName} 包含非法字符，已跳过", toolName);
                                continue;  // 跳过这个工具，不保存
                            }

                            JsonObject inputSchema = item["inputSchema"]?.AsObject() ?? new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject(),
                                ["required"] = new JsonArray()
                            };

                            JsonObject properties = inputSchema["properties"] as JsonObject ?? new JsonObject();
                            JsonArray requiredProperties = inputSchema["required"] as JsonArray ?? new JsonArray();

                            List<KernelParameterMetadata> kernelParameters = new List<KernelParameterMetadata>();

                            foreach (var property in properties)
                            {
                                if (property.Value is JsonObject propObj)
                                {
                                    string propName = property.Key;
                                    string propDescription = propObj["description"]?.GetValue<string>() ?? string.Empty;

                                    KernelParameterMetadata kernelParameter = new KernelParameterMetadata(propName)
                                    {
                                        Description = propDescription,
                                        IsRequired = requiredProperties.Contains(propName),
                                        Schema = KernelJsonSchema.Parse(propObj.ToJsonString())
                                    };
                                    kernelParameters.Add(kernelParameter);
                                }
                            }
                            string jgtoolName = this.SanitizeToolName(toolName);
                            KernelFunctionFromMethodOptions functionOption = new KernelFunctionFromMethodOptions
                            {
                                FunctionName = jgtoolName,
                                Description = toolDescription,
                                Parameters = kernelParameters,
                                AdditionalMetadata = new ReadOnlyDictionary<string, object?>(this.AdditionalMetadataDic)
                            };
                            KernelFunction toolFunction = KernelFunctionFactory.CreateFromMethod(this._tempMethod, functionOption);

                            this.AddTool(jgtoolName, toolFunction);
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ToolAdded, jgtoolName);
                        }

                        this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ToolCount, toolsJson.Count);

                        // 检查是否有更多工具需要获取
                        string nextCursor = resultObj["nextCursor"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrEmpty(nextCursor))
                        {
                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_MoreTools, nextCursor);
                            await this.RequestToolsListAsync(nextCursor);
                        }
                        else
                        {
                            this.IsReady = true;

                            this.CurrentSession.PrivateProvider.Kernel.ImportPluginFromFunctions(this.ModelName, this._mcpTools.Values);

                            this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ClientReady);
                        }

                        return;
                    }
                    else
                    {
                        this.Logger.LogWarning(Lang.BaseMcpClient_HandleMcpMessageAsync_GetToolsFailed);
                        return;
                    }
                }
            }
            else if (payloadObj.TryGetPropertyValue("method", out var method) && method is not null)
            {
                this.Logger.LogInformation(Lang.BaseMcpClient_HandleMcpMessageAsync_ClientRequest, method.GetValue<string>());
            }
            else if (payloadObj.TryGetPropertyValue("error", out var error) && error is not null)
            {
                var errorMsg = error["message"]?.GetValue<string>() ?? "未知错误";

                this.Logger.LogError(Lang.BaseMcpClient_HandleMcpMessageAsync_ErrorResponse, errorMsg);

                if (payloadObj.TryGetPropertyValue("id", out var msgId) && msgId is not null)
                {
                    this.RejectCallResult(msgId.GetValue<int>(), string.Format(Lang.BaseMcpClient_HandleMcpMessageAsync_ErrorResponse_Exception, errorMsg));
                }
            }
        }
        /// <summary>
        /// 发送MCP初始化请求异步方法
        /// </summary>
        /// <returns>异步任务</returns>
        public virtual async Task SendMcpInitializeAsync()
        {
            McpClientOptions mcpClientOptions = new McpClientOptions
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new ClientCapabilities
                {
                    Roots = new RootsCapability { ListChanged = true },
                    Sampling = new SamplingCapability { }
                },
                ClientInfo = new Implementation
                {
                    Name = this.ModelName,
                    Version = "1.0.0"
                }
            };
            JsonRpcRequest request = new JsonRpcRequest
            {
                Method = RequestMethods.Initialize,
                Id = new RequestId(1),
                Params = mcpClientOptions.ToNode()
            };

            this.Logger.LogInformation(Lang.BaseMcpClient_SendMcpInitializeAsync_SendingInit, this.CurrentSession.DeviceId);

            await this.SendMCPMessageAsync(request);
        }

        /// <summary>
        /// 发送MCP通知异步方法
        /// </summary>
        /// <param name="method">通知方法名</param>
        /// <returns>异步任务</returns>
        public virtual async Task SendMcpNotificationAsync(string method)
        {
            var @params = new { };
            JsonRpcNotification request = new JsonRpcNotification
            {
                Method = method,
                Params = @params.ToNode()
            };

            this.Logger.LogDebug(Lang.BaseMcpClient_SendMcpNotificationAsync_SendingNotification, this.CurrentSession.DeviceId, method);

            await this.SendMCPMessageAsync(request);
        }

        /// <summary>
        /// 请求工具列表异步方法
        /// </summary>
        /// <returns>异步任务</returns>
        public virtual async Task RequestToolsListAsync()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                Method = RequestMethods.ToolsList,
                Id = new RequestId(2)
            };

            this.Logger.LogDebug(Lang.BaseMcpClient_RequestToolsListAsync_RequestTools, this.CurrentSession.DeviceId);

            await this.SendMCPMessageAsync(request);
        }

        /// <summary>
        /// 使用游标请求工具列表异步方法
        /// </summary>
        /// <param name="cursor">分页游标</param>
        /// <returns>异步任务</returns>
        public virtual async Task RequestToolsListAsync(string cursor)
        {
            var @params = new { cursor };
            JsonRpcRequest request = new JsonRpcRequest
            {
                Method = RequestMethods.ToolsList,
                Id = new RequestId(2),
                Params = @params.ToNode()
            };

            this.Logger.LogDebug(Lang.BaseMcpClient_RequestToolsListAsync_RequestToolsWithCursor, this.CurrentSession.DeviceId, cursor);

            await this.SendMCPMessageAsync(request);
        }

        /// <summary>
        /// 检查是否存在指定名称的工具
        /// </summary>
        /// <param name="toolName">工具名称</param>
        /// <returns>是否存在该工具</returns>
        public bool HasTool(string toolName)
        {
            try
            {
                this._lockerSlim.Wait();
                return this._mcpTools.ContainsKey(toolName);
            }
            finally
            {
                this._lockerSlim.Release();
            }
        }

        /// <summary>
        /// 调用MCP工具异步方法
        /// </summary>
        /// <param name="toolName">工具名称</param>
        /// <param name="arguments">工具参数</param>
        /// <param name="timeout">超时时间（秒）</param>
        /// <returns>调用结果字符串</returns>
        public async virtual Task<string> CallMcpToolAsync(string toolName, KernelArguments arguments, int timeout = 30)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                throw new Exception("Invalid and empty tool name.");
            }
            if (!this.IsReady)
            {
                throw new Exception("The client is not ready yet.");
            }
            if (!this.HasTool(toolName))
            {
                throw new Exception($"The tool ({toolName}) is not registered yet.");
            }
            int toolCallId = this.NextId;
            Task<JsonObject> resultTask = this.RegisterCallResultAsync(toolCallId);

            string argJson = arguments.ToJson();

            if (this._mcpTools.TryGetValue(toolName, out KernelFunction? mcpTool))
            {
                string savedToolName = mcpTool.Name;
                string clientToolName = savedToolName.Replace(DOT_PLACEHOLDER, REAL_DOT);

                this.Logger.LogDebug("工具名转换: {Saved} -> {Client}", savedToolName, clientToolName);

                // 🔥 处理参数格式：将字符串数字转换为真正的数字，并创建JsonObject
                var argsObject = new JsonObject();
                foreach (var arg in arguments)
                {
                    if (arg.Value is string strValue && int.TryParse(strValue, out int intValue))
                    {
                        // 如果是数字字符串，作为数字添加
                        argsObject[arg.Key] = JsonValue.Create(intValue);
                        this.Logger.LogDebug("参数转换: {Key} = '{Str}' -> {Int} (数字)", arg.Key, strValue, intValue);
                    }
                    else if (arg.Value != null)
                    {
                        // 其他类型直接添加
                        argsObject[arg.Key] = JsonValue.Create(arg.Value);
                    }
                }

                var @params = new JsonObject
                {
                    ["name"] = clientToolName,
                    ["arguments"] = argsObject
                };

                JsonRpcRequest request = new JsonRpcRequest
                {
                    Id = new RequestId(toolCallId),
                    Method = RequestMethods.ToolsCall,
                    Params = @params
                };

                this.Logger.LogDebug("设备 {DeviceId} 调用 MCP 工具：{ToolName}，参数：{Arguments}",
                    this.CurrentSession.DeviceId, clientToolName, argsObject.ToJsonString());

                await this.SendMCPMessageAsync(request);
            }

            try
            {
                // 等待响应，设置超时
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
                Task completedTask = await Task.WhenAny(resultTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    this.Logger.LogError(Lang.BaseMcpClient_CallMcpToolAsync_Timeout, timeout, toolName);
                    throw new TimeoutException(string.Format(Lang.BaseMcpClient_CallMcpToolAsync_TimeoutEx, timeout));
                }

                JsonObject rawResult = await resultTask;
                this.Logger.LogDebug(Lang.BaseMcpClient_CallMcpToolAsync_CallSuccess, this.CurrentSession.DeviceId, rawResult.ToJsonString());

                if (rawResult.TryGetPropertyValue("isError", out var isErrorNode) && isErrorNode is not null && isErrorNode.GetValue<bool>())
                {
                    var errorMsg = rawResult?["error"]?.GetValue<string>() ?? "The tool call returned an error, but no specific error information was provided.";
                    throw new Exception(string.Format(Lang.BaseMcpClient_CallMcpToolAsync_ToolError, errorMsg));
                }

                if (rawResult.TryGetPropertyValue("content", out var contentNode) && contentNode is not null && contentNode is JsonArray content && content.Any())
                {
                    var firstItem = content.First();
                    if (firstItem is JsonObject first && first.TryGetPropertyValue("text", out var textNode) && textNode is not null)
                    {
                        return textNode.GetValue<string>();
                    }
                }

                return JsonHelper.Serialize(rawResult);
            }
            catch (TimeoutException timeoutException)
            {
                this.CleanCallResults(toolCallId);
                this.Logger.LogError(timeoutException, Lang.BaseMcpClient_CallMcpToolAsync_WaitTimeout, toolName, argJson);
                throw;
            }
            catch (Exception e)
            {
                this.CleanCallResults(toolCallId);
                this.Logger.LogError(e, Lang.BaseMcpClient_CallMcpToolAsync_CallFailed, toolName, argJson);
                throw;
            }
        }
        /// <summary>
        /// 更新该设备的三方工具（供 ThirdPartyToolRegistrar 调用）
        /// </summary>
        public async Task UpdateThirdPartyToolsAsync()
        {
            try
            {
                if (this.CurrentSession?.PrivateProvider?.Kernel == null)
                {
                    this.Logger.LogWarning("Kernel 未初始化，无法更新三方工具");
                    return;
                }

                // 获取该设备绑定的所有三方工具
                var thirdPartyTools = _toolRegistry.GetDeviceTools(this.CurrentSession.DeviceId);

                if (thirdPartyTools == null || !thirdPartyTools.Any())
                {
                    this.Logger.LogDebug("设备 {DeviceId} 没有绑定的三方工具", this.CurrentSession.DeviceId);
                    return;
                }

                var thirdPartyFunctions = new List<KernelFunction>();

                foreach (var tool in thirdPartyTools)
                {
                    // 工具名转换：点号替换为占位符
                    string functionName = tool.Name.Replace(REAL_DOT, DOT_PLACEHOLDER);

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

                    // 创建 KernelFunction - 使用同步包装异步调用
                    var function = KernelFunctionFactory.CreateFromMethod(
                        (KernelArguments args) =>
                        {
                            // 使用 Task.Run 包装异步调用，返回 Task<string>
                            return Task.Run(async () =>
                            {
                                var result = await _toolRegistry.CallThirdPartyToolAsync(tool.Name, args);

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
                            });
                        },
                        functionName,
                        tool.Description,
                        parameters);

                    thirdPartyFunctions.Add(function);
                }

                // 移除旧的 ThirdPartyService 插件
                var kernel = this.CurrentSession.PrivateProvider.Kernel;
                if (kernel.Plugins.TryGetPlugin("ThirdPartyService", out var oldPlugin))
                {
                    kernel.Plugins.Remove(oldPlugin);
                }

                // 注册新的插件
                if (thirdPartyFunctions.Any())
                {
                    kernel.ImportPluginFromFunctions("ThirdPartyService", thirdPartyFunctions);
                    this.Logger.LogInformation("设备 {DeviceId} 已更新 {Count} 个三方工具",
                        this.CurrentSession.DeviceId, thirdPartyFunctions.Count);
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "更新设备 {DeviceId} 的三方工具失败", this.CurrentSession.DeviceId);
            }
        }
        /// <summary>
        /// 抽象方法：发送MCP消息
        /// </summary>
        /// <typeparam name="TMessage">消息类型</typeparam>
        /// <param name="message">消息对象</param>
        /// <returns>异步任务</returns>
        protected abstract Task SendMCPMessageAsync<TMessage>(TMessage message);

        /// <summary>
        /// 添加工具到工具集合中
        /// </summary>
        /// <param name="toolName">工具名称</param>
        /// <param name="toolFunction">工具函数</param>
        protected void AddTool(string toolName, KernelFunction toolFunction)
        {
            try
            {
                this._lockerSlim.Wait();
                if (this._mcpTools.ContainsKey(toolName))
                {
                    this.Logger.LogWarning(Lang.BaseMcpClient_AddTool_ToolExists, toolName);
                    return;
                }
                this._mcpTools.Add(toolName, toolFunction);
            }
            finally
            {
                this._lockerSlim.Release();
            }
        }

        /// <summary>
        /// 注册调用结果异步方法
        /// </summary>
        /// <param name="id">调用ID</param>
        /// <returns>结果任务</returns>
        protected virtual Task<JsonObject> RegisterCallResultAsync(int id)
        {
            TaskCompletionSource<JsonObject> tcs = new TaskCompletionSource<JsonObject>();
            this._callResults.TryAdd(id, tcs);
            return tcs.Task;
        }

        /// <summary>
        /// 解析调用结果
        /// </summary>
        /// <param name="id">调用ID</param>
        /// <param name="result">结果对象</param>
        protected virtual void ResolveCallResult(int id, JsonObject result)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject>? tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(result);

                this.CleanCallResults(id);
            }
        }

        /// <summary>
        /// 拒绝调用结果
        /// </summary>
        /// <param name="id">调用ID</param>
        /// <param name="errorMessage">错误消息</param>
        protected virtual void RejectCallResult(int id, string errorMessage)
        {
            if (this._callResults.TryGetValue(id, out TaskCompletionSource<JsonObject>? tcs) && !tcs.Task.IsCompleted)
            {
                tcs.SetException(new Exception(errorMessage));
            }
        }

        /// <summary>
        /// 清理调用结果
        /// </summary>
        /// <param name="id">调用ID</param>
        /// <returns>是否成功移除</returns>
        protected virtual bool CleanCallResults(int id)
        {
            return this._callResults.Remove(id);
        }

        /// <summary>
        /// 初始化会话
        /// </summary>
        /// <param name="config">MCP客户端构建配置</param>
        protected void InitSession(MCPClientBuildConfig config)
        {
            this.CurrentSession = config.Session;
            this.AdditionalMetadataDic = new Dictionary<string, object?>
        {
            { "session_id", this.CurrentSession.SessionId }
        };
        }

        /// <summary>
        /// 清理工具名称中的非法字符
        /// </summary>
        /// <param name="name">原始名称</param>
        /// <returns>清理后的名称</returns>
        private string SanitizeToolName(string name)
        {
            string processed = name.Replace(REAL_DOT, DOT_PLACEHOLDER);
            return Regex.Replace(processed, @"[^a-zA-Z0-9_\\-\u4e00-\u9fff]", "_");
        }


    }
}
