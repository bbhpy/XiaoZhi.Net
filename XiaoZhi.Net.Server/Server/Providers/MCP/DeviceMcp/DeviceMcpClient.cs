using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Providers.MCP.DeviceMcp
{
  /// <summary>
/// 设备MCP客户端类，继承自BaseMcpClient并实现ISubMcpClient接口
/// 用于处理设备相关的MCP通信
/// </summary>
internal class DeviceMcpClient : BaseMcpClient<DeviceMcpClient>, ISubMcpClient
{
    private string? _visionUrl;
    private string? _visionToken;

    /// <summary>
    /// 构造函数，初始化DeviceMcpClient实例
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public DeviceMcpClient(ILogger<DeviceMcpClient> logger, ToolRouter toolRegistry,McpServiceStore mcpServiceStore) : base(logger, toolRegistry, mcpServiceStore)
        {
    }

    /// <summary>
    /// 获取模型名称，返回设备MCP客户端类型名称
    /// </summary>
    public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
    
    /// <summary>
    /// 获取提供者类型，返回"SubMcpClient"
    /// </summary>
    public override string ProviderType => "SubMcpClient";

    /// <summary>
    /// 构建MCP客户端配置
    /// </summary>
    /// <param name="config">MCP客户端构建配置对象</param>
    /// <returns>构建是否成功，始终返回true</returns>
    public override bool Build(MCPClientBuildConfig config)
    {
        this.InitSession(config);
        ModelSetting modelSetting = config.ModelSetting;

        // 从配置中获取视觉URL和令牌
        this._visionUrl = modelSetting.Config.GetConfigValueOrDefault("VisionUrl", string.Empty);
        this._visionToken = modelSetting.Config.GetConfigValueOrDefault("VisionToken", string.Empty);

        // 异步发送MCP初始化请求和工具列表请求
        this.SendMcpInitializeAsync().ConfigureAwait(false);
        this.RequestToolsListAsync().ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 异步发送MCP初始化请求
    /// </summary>
    /// <returns>异步任务</returns>
    public override async Task SendMcpInitializeAsync()
    {

        var vision = new
        {
            Url = this._visionUrl,
            Token = this._visionToken
        };

        var @params = new
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new
            {
                Roots = new
                {
                    ListChanged = true
                },
                Sampling = new { },
                Vision = vision
            },
            clientInfo = new
            {
                Name = this.ProviderType,
                Version = "1.0.0"
            }
        };

        JsonRpcRequest request = new JsonRpcRequest
        {
            Method = RequestMethods.Initialize,
            Id = new RequestId(1),
            Params = @params.ToNode()
        };
        this.Logger.LogInformation(Lang.DeviceMcpClient_SendMcpInitializeAsync_SendingInit, this.CurrentSession.SessionId);
        await this.SendMCPMessageAsync(request);
    }

    /// <summary>
    /// 异步发送MCP消息
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="message">要发送的消息对象</param>
    /// <returns>异步任务</returns>
    /// <exception cref="ArgumentNullException">当message参数为null时抛出</exception>
    protected override async Task SendMCPMessageAsync<TMessage>(TMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message), "Message cannot be null.");
        }
        var mcpMessage = new
        {
            Type = "mcp",
            Payload = message
        };
        string jsonMessage = mcpMessage.ToJson();
            await this.CurrentSession.SendOutter.SendAsync(jsonMessage, "mcp");
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {

    }
}
}
