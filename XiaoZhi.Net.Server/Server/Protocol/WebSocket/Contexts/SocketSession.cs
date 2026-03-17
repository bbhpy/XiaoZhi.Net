using Microsoft.Extensions.Logging;
using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Contexts
{
    /// <summary>
    /// Socket会话类，继承自WebSocketSession并实现IBizSendOutter接口
    /// 负责处理与客户端的WebSocket连接和消息传输
    /// </summary>
    internal class SocketSession : WebSocketSession, IBizSendOutter
    {
        private readonly HandlerManager _handlerManager;
        private readonly ProviderManager _providerManager;

        /// <summary>
        /// 初始化SocketSession实例
        /// </summary>
        /// <param name="handlerManager">处理器管理器</param>
        /// <param name="providerManager">提供者管理器</param>
        public SocketSession(HandlerManager handlerManager, ProviderManager providerManager)
        {
            this._handlerManager = handlerManager;
            this._providerManager = providerManager;
        }

        /// <summary>
        /// 小知会话对象，用于存储当前连接的会话信息
        /// </summary>
        public Session? XiaoZhiSession { get; set; }

        #region ISendOutter
        /// <summary>
        /// 获取会话ID
        /// </summary>
        public string SessionId => this.SessionID;

        /// <summary>
        /// 获取当前小知会话对象
        /// </summary>
        /// <returns>Session对象</returns>
        /// <exception cref="SessionNotInitializedException">当XiaoZhiSession为null时抛出异常</exception>
        public Session GetSession()
        {
            if (this.XiaoZhiSession is null)
            {
                throw new SessionNotInitializedException();
            }
            else
            {
                return this.XiaoZhiSession;
            }
        }

        /// <summary>
        /// 异步发送JSON字符串到客户端
        /// </summary>
        /// <param name="json">要发送的JSON字符串</param>
        /// <returns>异步任务</returns>
        public Task SendAsync(string json,string topic = "")
        {
            // 记录发送JSON日志
            this.Logger.LogDebug(Lang.SocketSession_SendAsync_SendingJson, this.XiaoZhiSession?.DeviceId, Regex.Unescape(!string.IsNullOrEmpty(json) ? json : string.Empty));
            //this.Logger.LogInformation("发送JSON给客户端ID为：{SessionId} Json为: {Json}", SessionId ,json);
            return base.SendAsync(json).AsTask();
        }

        /// <summary>
        /// 异步发送音频数据包到客户端
        /// </summary>
        /// <param name="opusPacket">Opus音频数据包</param>
        /// <returns>异步任务</returns>
        public Task SendAsync(byte[] opusPacket)
        {
            return base.SendAsync(opusPacket).AsTask();
        }

        /// <summary>
        /// 异步发送TTS状态消息到客户端
        /// </summary>
        /// <param name="state">TTS状态</param>
        /// <param name="text">文本内容（可选）</param>
        /// <returns>异步任务</returns>
        public Task SendTtsMessageAsync(TtsStatus state, string? text = null)
        {
            if (this.XiaoZhiSession is null || this.XiaoZhiSession.PrivateProvider.Tts is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendTtsMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new Dictionary<string, string>
            {
                ["type"] = "tts",
                ["state"] = state.GetDescription(),
                ["session_id"] = this.SessionId
            };

            // 当状态为开始时，添加采样率信息
            if (state == TtsStatus.Start)
            {
                msg["sample_rate"] = this.XiaoZhiSession.PrivateProvider.Tts.GetTtsSampleRate().ToString();
            }

            if (!string.IsNullOrEmpty(text))
            {
                msg["text"] = text;
            }

            string json = JsonHelper.Serialize(msg);

            // 当状态为停止时，重置会话
            if (state == TtsStatus.Stop)
            {
                this.XiaoZhiSession.Reset();
            }
            return this.SendAsync(json);
        }

        /// <summary>
        /// 异步发送语音识别结果消息到客户端
        /// </summary>
        /// <param name="sttText">语音识别文本</param>
        /// <returns>异步任务</returns>
        public Task SendSttMessageAsync(string sttText)
        {
            if (this.XiaoZhiSession is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendSttMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new
            {
                Type = "stt",
                Text = sttText,
                SessionId = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(msg));
        }

        /// <summary>
        /// 异步发送情感消息到客户端
        /// </summary>
        /// <param name="emotion">情感类型</param>
        /// <returns>异步任务</returns>
        public Task SendLlmMessageAsync(Emotion emotion)
        {
            if (this.XiaoZhiSession is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendLlmMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var emo = new
            {
                Type = "llm",
                Text = emotion.GetDescription(),
                Emotion = emotion.GetName().ToLower(),
                SessionId = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(emo));
        }

        /// <summary>
        /// 异步发送中止消息到客户端
        /// </summary>
        /// <returns>异步任务</returns>
        public Task SendAbortMessageAsync()
        {
            if (this.XiaoZhiSession is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendAbortMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var abortMessage = new
            {
                type = "tts",
                state = "stop",
                session_id = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(abortMessage));
        }

        /// <summary>
        /// 异步关闭会话连接
        /// </summary>
        /// <param name="reason">关闭原因</param>
        /// <returns>异步任务</returns>
        public Task CloseSessionAsync(string reason = "")
        {
            return this.CloseAsync(CloseReason.NormalClosure, reason).AsTask();
        }
        #endregion

        /// <summary>
        /// 会话连接建立时的回调方法
        /// </summary>
        /// <returns>异步值任务</returns>
        protected override ValueTask OnSessionConnectedAsync()
        {
            // 从HTTP头获取设备ID、授权令牌和用户端点信息
            string deviceId = this.HttpHeader.Items.Get("device-id")!;
            string token = this.HttpHeader.Items.Get("authorization")!;
            IPEndPoint userEndPoint = (this.RemoteEndPoint as IPEndPoint)!;

            // 创建新的会话对象
            Session session = new Session(this.SessionId, deviceId, token, userEndPoint, Session.ProtocolType.websocket, this);

            // 初始化问候消息处理器
            this._handlerManager.InitializeHelloMessageHandler(session);

            // 刷新最后活动时间
            session.RefreshLastActivityTime();

            // 设置当前小知会话
            this.XiaoZhiSession = session;

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 会话关闭时的回调方法
        /// </summary>
        /// <param name="e">关闭事件参数</param>
        /// <returns>异步值任务</returns>
        protected override async ValueTask OnSessionClosedAsync(SuperSocket.Connection.CloseEventArgs e)
        {
            if (this.XiaoZhiSession is not null)
            {
                // 保存会话内存数据
                await this._providerManager.SaveMemoryAsync(this.XiaoZhiSession);

                // 释放会话资源
                this.XiaoZhiSession.Release();

                // 记录客户端离线日志
                this.Logger.LogDebug(Lang.SocketSession_OnSessionClosedAsync_ClientOffline, this.XiaoZhiSession.DeviceId, this.XiaoZhiSession.SessionId, e.Reason);
            }
        }
    }
}
