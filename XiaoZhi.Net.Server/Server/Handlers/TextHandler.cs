using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Providers.MCP;

namespace XiaoZhi.Net.Server.Handlers
{
 /// <summary>
/// 文本处理器，继承自BaseHandler并实现IOutHandler<string>接口
/// 负责处理各种类型的文本消息，包括中止、监听、IoT和MCP消息
/// </summary>
internal class TextHandler : BaseHandler, IOutHandler<string>
{
    private readonly ProviderManager _providerManager;
    private readonly ObjectPool<Workflow<string>> _workflowPool;

    /// <summary>
    /// 初始化TextHandler实例
    /// </summary>
    /// <param name="providerManager">提供者管理器</param>
    /// <param name="workflowPool">工作流对象池</param>
    /// <param name="config">小智配置</param>
    /// <param name="logger">日志记录器</param>
    public TextHandler(ProviderManager providerManager, 
        ObjectPool<Workflow<string>> workflowPool,
        XiaoZhiConfig config, 
        ILogger<TextHandler> logger) : base(config, logger)
    {
        this._providerManager = providerManager;
        this._workflowPool = workflowPool;
    }
    
    /// <summary>
    /// 手动停止事件
    /// </summary>
    public event Action<Session>? OnManualStop;
    
    /// <summary>
    /// 获取处理器名称
    /// </summary>
    public override string HandlerName => nameof(TextHandler);
    
    /// <summary>
    /// 下一个写入器
    /// </summary>
    public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

    /// <summary>
    /// 构建处理器
    /// </summary>
    /// <param name="privateProvider">私有提供者</param>
    /// <returns>构建结果</returns>
    public override bool Build(PrivateProvider privateProvider)
    {
        this.RegisterCancellationToken();
        return true;
    }

    /// <summary>
    /// 处理文本数据
    /// </summary>
    /// <param name="data">要处理的文本数据</param>
    public async void Handle(string data)
    {
        JsonNode? jsonObject = JsonNode.Parse(data);

        // 判断是否是整数
        if (jsonObject is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
        {
            await this.SendOutter.SendAsync(intValue.ToString(), "text");
            return;
        }

#if DEBUG
        this.Logger.LogDebug(Lang.TextHandler_Handle_ReceivedText, jsonObject?.ToJsonString());
#endif

        if (jsonObject is JsonObject jsonObj)
        {
            string? type = jsonObj["type"]?.GetValue<string>()?.ToLower();
            if (string.IsNullOrEmpty(type))
            {
                this.Logger.LogError(Lang.TextHandler_Handle_InvalidType);
                return;
            }

            switch (type)
            {
                case "abort":
                    await this.HandleAbortMessage();
                    break;
                case "listen":
                    this.HandleListen(jsonObj);
                    break;
                case "iot":
                    this.HandleIotDescriptors(jsonObj);
                    break;
                case "mcp":
                    await Task.Run(() =>
                    {
                        this.HandleMcp(jsonObj);
                    }).ConfigureAwait(false);

                    break;
            }
        }
    }

    /// <summary>
    /// 处理中止消息
    /// </summary>
    /// <returns>异步任务</returns>
    private async Task HandleAbortMessage()
    {
        Session session = this.SendOutter.GetSession();
        this.Logger.LogInformation(Lang.TextHandler_HandleAbortMessage_Received);
        await this.SendOutter.SendAbortMessageAsync();
        session.Abort();
        this.Logger.LogInformation(Lang.TextHandler_HandleAbortMessage_Cancelled);
    }

    /// <summary>
    /// 处理监听消息
    /// </summary>
    /// <param name="jsonObject">JSON对象</param>
    private async void HandleListen(JsonObject jsonObject)
    {
        Session session = this.SendOutter.GetSession();
        string? mode = jsonObject["mode"]?.GetValue<string>()?.ToLower();
        if (!string.IsNullOrEmpty(mode))
        {
            session.SetListenMode(mode);
            this.Logger.LogInformation(Lang.TextHandler_HandleListen_ModeSetting, mode);
        }

        string? state = jsonObject["state"]?.GetValue<string>()?.ToLower();
        if (!string.IsNullOrEmpty(state))
        {
            if (state == "start")
            {
                session.ManualStart();
            }
            else if (state == "stop")
            {
                session.ManualStop();
                this.OnManualStop?.Invoke(session);
            }
            else if (state == "detect")
            {
                string? text = jsonObject["text"]?.GetValue<string>()?.ToLower();
                if (!string.IsNullOrEmpty(text))
                {
                    var workflow = this._workflowPool.Get();
                    workflow.Initialize(session, text);
                    await this.NextWriter.WriteAsync(workflow);
                }
            }
        }
    }

    /// <summary>
    /// 处理IoT描述符消息
    /// </summary>
    /// <param name="jsonObject">JSON对象</param>
    private void HandleIotDescriptors(JsonObject jsonObject)
    {
        Session session = this.SendOutter.GetSession();
        if (!session.PrivateProvider.HasIoT)
        {
            this._providerManager.BuildIoT(session);
        }

        if (session.PrivateProvider.IoTClient is null)
        {
            this.Logger.LogError(Lang.TextHandler_HandleIotDescriptors_ClientNotInit, session.DeviceId);
            return;
        }
        session.PrivateProvider.IoTClient.HandleIoTMessage(jsonObject);
    }

    /// <summary>
    /// 处理MCP消息
    /// </summary>
    /// <param name="jsonObject">JSON对象</param>
    private async void HandleMcp(JsonObject jsonObject)
    {
        if (jsonObject.TryGetPropertyValue("payload", out var payload) && payload is not null && payload is JsonObject payloadObj)
        {
            Session session = this.SendOutter.GetSession();
            ISubMcpClient? subMcpClient = session.PrivateProvider.McpClient?.GetSubMcpClient(SubMCPClientTypeNames.DeviceMcpClient);
            if (subMcpClient is not null)
            {
                await subMcpClient.HandleMcpMessageAsync(payloadObj);
            }
            else
            {
                this.Logger.LogError(Lang.TextHandler_HandleMcp_ClientNotFound, session.SessionId);
            }
        }

    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        this.NextWriter.Complete();
        base.Dispose();
    }
}
}
