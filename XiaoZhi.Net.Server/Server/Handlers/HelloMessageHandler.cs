using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Handlers
{
 /// <summary>
/// 处理Hello消息的处理器类，继承自BaseHandler
/// </summary>
internal class HelloMessageHandler : BaseHandler
{
    private const string DEFAULT_AUDIO_FORMAT = "opus";
    private readonly ProviderManager _providerManager;
    private readonly HandlerManager _handlerManager;
        private readonly DialogueHandler _dialogueHandler;

        /// <summary>
        /// 初始化HelloMessageHandler实例
        /// </summary>
        /// <param name="providerManager">提供者管理器</param>
        /// <param name="handlerManager">处理器管理器</param>
        /// <param name="config">小智配置对象</param>
        /// <param name="logger">日志记录器</param>
        public HelloMessageHandler(ProviderManager providerManager, HandlerManager handlerManager, XiaoZhiConfig config,
        ILogger<HelloMessageHandler> logger, DialogueHandler dialogueHandler) : base(config, logger)
        {
            this._providerManager = providerManager;
            this._handlerManager = handlerManager;
            this._dialogueHandler = dialogueHandler;
        }

    /// <summary>
    /// 获取处理器名称
    /// </summary>
    public override string HandlerName => nameof(HelloMessageHandler);


    /// <summary>
    /// 构建处理器
    /// </summary>
    /// <param name="privateProvider">私有提供者</param>
    /// <returns>构建结果</returns>
    public override bool Build(PrivateProvider privateProvider)
    {
        this.Builded = true;
        return true;
    }

    /// <summary>
    /// 处理Hello消息
    /// </summary>
    /// <param name="helloMessage">Hello消息JSON对象</param>
    public async Task Handle(JsonObject helloMessage)
    {
        Session session = this.SendOutter.GetSession();

        HelloMessage defaultHelloMessage = new HelloMessage(this.SendOutter.SessionId, this.Config.ServerProtocol.GetDescription().ToLower(), this.Config.AudioSetting);

        // 解析音频参数并更新会话设置
        if (helloMessage.TryGetPropertyValue("audio_params", out var audioParams) && audioParams is not null)
        {
            JsonObject audioParamsObj = audioParams.AsObject();
            string format = audioParamsObj["format"]?.GetValue<string>() ?? DEFAULT_AUDIO_FORMAT;
            int sampleRate = audioParamsObj["sample_rate"]?.GetValue<int>() ?? 16000;
            int channels = audioParamsObj["channels"]?.GetValue<int>() ?? 1;
            int frameDuration = audioParamsObj["frame_duration"]?.GetValue<int>() ?? 60;

            session.AudioSetting.Format = format;
            session.AudioSetting.SampleRate = sampleRate;
            session.AudioSetting.Channels = channels;
            session.AudioSetting.FrameDuration = frameDuration;
        }
        
        bool providerInitResult = await this._providerManager.InitializePrivateConfigAsync(session);
        bool handlerInitResult = this._handlerManager.InitializePrivateConfig(session);

        if (providerInitResult && handlerInitResult)
        {
                if (session.protocolType == Session.ProtocolType.websocket)
                {
                    await this.SendOutter.SendAsync(JsonHelper.Serialize(defaultHelloMessage), string.Empty);
                }
                else
                {
                    await this.SendOutter.SendAsync(JsonHelper.Serialize(defaultHelloMessage), "hello");
                }

            // 检查并处理MCP功能支持
            if (helloMessage.TryGetPropertyValue("features", out var features) && features is not null)
            {
                JsonObject featuresObj = features.AsObject();
                if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp is not null)
                {
                    bool isSupportMCP = mcp.GetValue<bool>();
                    if (isSupportMCP)
                    {
                        this._providerManager.BuildMCP(session);
                    }
                }
            }

                // 你想直接给AI对话，不走语音识别那套
                //string yourText = "限制10个字内；简短的打个招呼。";

                // 直接调用 dialogueHandler，完全跳过前面所有步骤
                //await _dialogueHandler.Handle(yourText, session);
                //await _dialogueHandler.call(yourText, session);
            }
        else
        {
            this.Logger.LogError(Lang.HelloMessageHandler_Handle_InitFailed, session.DeviceId);
        }
    }
    }
}
