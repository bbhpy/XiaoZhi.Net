using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol;
using SystemWebSocket = System.Net.WebSockets.WebSocket;

namespace XiaoZhi.Net.Server.Server.Protocol.WebSocket
{
    /// <summary>
    /// WebSocket 会话类，实现 IBizSendOutter 接口
    /// 替代原来的 SocketSession
    /// </summary>
    internal class SocketSession : IBizSendOutter
    {
        private readonly string _sessionId;
        private readonly SystemWebSocket _webSocket;
        private readonly HandlerManager _handlerManager;
        private readonly ProviderManager _providerManager;
        private readonly SocketSessionStore _connectionStore;
        private readonly ILogger _logger;
        private readonly IDictionary<string, string> _headers;
        private readonly IPEndPoint? _remoteEndPoint;
        /// <summary>
        /// 小知会话对象，用于存储当前连接的会话信息
        /// </summary>
        public Session? XiaoZhiSession { get; set; }
        private CancellationTokenSource? _cts;
        private bool _isClosed; 
        private readonly IServiceProvider _serviceProvider;

        public string SessionId => _sessionId;

        public SocketSession(
            string sessionId,
            SystemWebSocket webSocket,
            HandlerManager handlerManager,
            ProviderManager providerManager,
            SocketSessionStore connectionStore,
            ILogger logger,
            IServiceProvider serviceProvider,
            IDictionary<string, string> headers,
            IPEndPoint? remoteEndPoint)
        {
            _sessionId = sessionId;
            _webSocket = webSocket;
            _handlerManager = handlerManager;
            _providerManager = providerManager;
            _connectionStore = connectionStore;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _headers = headers;
            _remoteEndPoint = remoteEndPoint;
            _cts = new CancellationTokenSource();
        }
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
        /// 握手验证（替代原来的 AuthenticationVerification）
        /// </summary>
        public async Task<bool> OnConnectingAsync()
        {
            // 从请求头获取设备ID和认证令牌  this.HttpHeader.Items.Get("device-id")
            string deviceId = _headers.TryGetValue("device-id", out var dId) ? dId : string.Empty;
            string token = _headers.TryGetValue("authorization", out var t) ? t : string.Empty;

            _logger.LogInformation("WebSocket 连接请求: DeviceId={DeviceId}, RemoteEndPoint={RemoteEndPoint}", deviceId, _remoteEndPoint);

            if (string.IsNullOrEmpty(deviceId))
            {
                _logger.LogError("设备ID为空，拒绝连接");
                return false;
            }
            // 获取配置
            var config = _serviceProvider.GetRequiredService<XiaoZhiConfig>();

            bool verifyResult = true;

            // 如果启用了认证
            if (config.AuthEnabled)
            {
                var basicVerify = _serviceProvider.GetService<IBasicVerify>();
                if (basicVerify != null)
                {
                    verifyResult = basicVerify.Verify(deviceId, token, _remoteEndPoint);
                }
            }

            if (!verifyResult)
            {
                _logger.LogError("设备认证失败: DeviceId={DeviceId}, IP={RemoteEndPoint}", deviceId, _remoteEndPoint);
                return false;
            }

            // 创建会话对象
            var session = new Session(_sessionId, deviceId, token, _remoteEndPoint!, Session.ProtocolType.websocket, this);
            // TODO: 设置 DeviceToken（如果需要）
            session.DeviceToken = "AAAFPzL146bfSelCIxiGaYP73orWydK4ZOuDCajDn4bMPNXeIzYhp8y3ScGAQt0Xa";

            // 初始化问候消息处理器
            _handlerManager.InitializeHelloMessageHandler(session);

            // 刷新最后活动时间
            session.RefreshLastActivityTime();

            // 保存会话
            XiaoZhiSession = session;
            _connectionStore.Add(_sessionId, session);

            _logger.LogInformation("WebSocket 会话建立成功: SessionId={SessionId}, DeviceId={DeviceId}", _sessionId, deviceId);

            return true;
        }

        /// <summary>
        /// 开始接收消息
        /// </summary>
        public async Task StartReceivingAsync()
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    using var ms = new System.IO.MemoryStream();

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var data = ms.ToArray();

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(data);
                        await HandleTextMessageAsync(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        await HandleBinaryMessageAsync(data);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收 WebSocket 消息时出错，SessionId: {SessionId}", _sessionId);
            }
            finally
            {
                await CloseSessionAsync("Connection closed");
            }
        }

        /// <summary>
        /// 处理文本消息（替代原来的 MessageDispatch 中文本部分）
        /// </summary>
        private async Task HandleTextMessageAsync(string message)
        {
            if (XiaoZhiSession is null) return;

            try
            {
                JsonNode? jsonObject = JsonNode.Parse(message);
                string? type = jsonObject?["type"]?.GetValue<string>()?.ToLower();

                if (jsonObject is JsonObject jsonObj && !string.IsNullOrEmpty(type) && type == "hello")
                {
                    _logger.LogInformation("客户端 {SessionId} 发送 Hello 消息: {Message}", _sessionId, message);
                    await XiaoZhiSession.HandlerPipeline.HandleHelloMessage(jsonObj);
                }
                else
                {
                    _logger.LogInformation("客户端 {SessionId} 发送文本消息: {Message}", _sessionId, message);
                    XiaoZhiSession.HandlerPipeline.HandleTextMessage(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理文本消息时出错，SessionId: {SessionId}", _sessionId);
            }
        }

        /// <summary>
        /// 处理二进制消息（替代原来的 MessageDispatch 中二进制部分）
        /// </summary>
        private async Task HandleBinaryMessageAsync(byte[] data)
        {
            if (XiaoZhiSession is null) return;

            try
            {
                await XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理二进制消息时出错，SessionId: {SessionId}", _sessionId);
            }
        }

        #region IBizSendOutter 实现

        public async Task SendAsync(string json, string topic = "")
        {
            if (_webSocket.State != WebSocketState.Open) return;

            _logger.LogDebug("发送 JSON 消息到客户端 {SessionId}: {Json}", _sessionId, json);

            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }

        public async Task SendAsync(byte[] bytePacket)
        {
            if (_webSocket.State != WebSocketState.Open) return;

            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytePacket),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);
        }

        public async Task SendTtsMessageAsync(TtsStatus state, string? text = null)
        {
            if (XiaoZhiSession is null || XiaoZhiSession.PrivateProvider.Tts is null)
            {
                _logger.LogError("发送 TTS 消息失败: 会话未初始化");
                return;
            }

            var msg = new Dictionary<string, string>
            {
                ["type"] = "tts",
                ["state"] = state.GetDescription(),
                ["session_id"] = _sessionId
            };

            if (state == TtsStatus.Start)
            {
                msg["sample_rate"] = XiaoZhiSession.PrivateProvider.Tts.GetTtsSampleRate().ToString();
            }

            if (!string.IsNullOrEmpty(text))
            {
                msg["text"] = text;
            }

            string json = JsonHelper.Serialize(msg);

            if (state == TtsStatus.Stop)
            {
                XiaoZhiSession.Reset();
            }

            await SendAsync(json);
        }

        public async Task SendSttMessageAsync(string sttText)
        {
            if (XiaoZhiSession is null) return;

            var msg = new
            {
                Type = "stt",
                Text = sttText,
                SessionId = _sessionId
            };

            await SendAsync(JsonHelper.Serialize(msg));
        }

        public async Task SendLlmMessageAsync(Emotion emotion)
        {
            if (XiaoZhiSession is null) return;

            var emo = new
            {
                Type = "llm",
                Text = emotion.GetDescription(),
                Emotion = emotion.GetName().ToLower(),
                SessionId = _sessionId
            };

            await SendAsync(JsonHelper.Serialize(emo));
        }

        public async Task SendAbortMessageAsync()
        {
            if (XiaoZhiSession is null) return;

            var abortMessage = new
            {
                type = "tts",
                state = "stop",
                session_id = _sessionId
            };

            await SendAsync(JsonHelper.Serialize(abortMessage));
        }

        public async Task CloseSessionAsync(string reason = "")
        {
            if (_isClosed) return;
            _isClosed = true;

            _cts?.Cancel();

            if (XiaoZhiSession != null)
            {
                // 保存会话内存数据
                await _providerManager.SaveMemoryAsync(XiaoZhiSession);
                // 释放会话资源
                XiaoZhiSession.Release();
                // 从存储中移除
                _connectionStore.Remove(_sessionId);
            }

            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        reason ?? "Normal closure",
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭 WebSocket 连接时出错，SessionId: {SessionId}", _sessionId);
            }
            finally
            {
                _webSocket.Dispose();
                _cts?.Dispose();
                _cts = null;
            }

            _logger.LogInformation("WebSocket 会话已关闭: SessionId={SessionId}, Reason={Reason}", _sessionId, reason);
        }

        #endregion

        /// <summary>
        /// 关闭连接（用于服务器主动关闭）
        /// </summary>
        public async Task CloseAsync(string reason = "")
        {
            await CloseSessionAsync(reason);
        }
    }
}

