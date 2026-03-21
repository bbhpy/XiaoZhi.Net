using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Providers.MCP.McpEndpoint
{
 /// <summary>
/// MCP端点客户端类，用于处理与MCP端点的WebSocket连接和消息通信
/// </summary>
internal class McpEndpointClient : BaseMcpClient<McpEndpointClient>, ISubMcpClient
{
    private string? _endpointUrl;
    private WebSocketClient? _webSocketClient;

    /// <summary>
    /// 初始化McpEndpointClient类的新实例
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public McpEndpointClient(ILogger<McpEndpointClient> logger, ToolRegistry toolRegistry) : base(logger, toolRegistry)
        {
    }

    /// <summary>
    /// 获取模型名称
    /// </summary>
    public override string ModelName => SubMCPClientTypeNames.McpEndpointClient;
    
    /// <summary>
    /// 获取提供程序类型
    /// </summary>
    public override string ProviderType => "SubMcpClient";

    /// <summary>
    /// 构建MCP客户端配置
    /// </summary>
    /// <param name="config">MCP客户端构建配置</param>
    /// <returns>如果构建成功返回true，否则返回false</returns>
    public override bool Build(MCPClientBuildConfig config)
    {
        try
        {
            this.InitSession(config);
            ModelSetting modelSetting = config.ModelSetting;

            this._endpointUrl = modelSetting.Config.GetConfigValueOrDefault("EndpointUrl");

            // 检查端点URL是否为空
            if (string.IsNullOrEmpty(this._endpointUrl))
            {
                this.Logger.LogWarning(Lang.McpEndpointClient_Build_UrlEmpty);
                return true;
            }

            Dictionary<string, string>? headers = modelSetting.Config.GetConfigValueOrDefault<Dictionary<string, string>>("Headers");
            this._webSocketClient = new WebSocketClient(headers);
            this._webSocketClient.OnOpen += this.WebSocketClientEngine_OnOpen;
            this._webSocketClient.OnTextMessage += this.WebSocketClient_OnMessage;

            this._webSocketClient.ConnectAsync(this._endpointUrl).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, Lang.McpEndpointClient_Build_InvalidSettings, this.ProviderType, this.ModelName);
            return false;
        }
    }

    /// <summary>
    /// 异步发送MCP消息
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="message">要发送的消息</param>
    /// <returns>异步任务</returns>
    protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message), "Message cannot be null.");
        }

        if (this._webSocketClient is null)
        {
            throw new InvalidOperationException("WebSocket client is not initialized.");
        }

        string json = message.ToJson();
        return this._webSocketClient.SendAsync(json);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        if (this._webSocketClient is null)
        {
            return;
        }
        this._webSocketClient.CloseAsync().ConfigureAwait(false);
    }
    
    /// <summary>
    /// WebSocket连接打开时的事件处理方法
    /// </summary>
    private async void WebSocketClientEngine_OnOpen()
    {
        await this.SendMcpInitializeAsync();
        await this.SendMcpNotificationAsync(NotificationMethods.InitializedNotification);
        await this.RequestToolsListAsync();

        this.Logger.LogInformation(Lang.McpEndpointClient_OnOpen_Connected);
    }

    /// <summary>
    /// WebSocket接收文本消息时的事件处理方法
    /// </summary>
    /// <param name="data">接收到的消息数据</param>
    private async void WebSocketClient_OnMessage(string data)
    {
        await this.HandleMcpEndpointMessage(data);
    }

    /// <summary>
    /// 处理MCP端点消息
    /// </summary>
    /// <param name="data">消息数据</param>
    /// <returns>异步任务</returns>
    private async Task HandleMcpEndpointMessage(string data)
    {
        try
        {
            JsonObject? jObj = JsonNode.Parse(data) as JsonObject;
            if (jObj is null)
            {
                return;
            }
            await this.HandleMcpMessageAsync(jObj);
        }
        catch (Exception)
        {

            throw;
        }
    }
}
}
