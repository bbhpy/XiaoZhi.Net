using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using MQTTnet.Server.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts;
using XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt
{
    /// <summary>
    /// MQTT+UDP 合并会话类
    /// 承载单设备的MQTT连接和UDP音频通道信息
    /// </summary>
    internal class MqttUdpSession: IBizSendOutter,IDisposable
    {
        /// <summary>
        /// 设备MAC地址 mqtt连接建立时 在MqttClientId解析出 WebSocket请求参数 Device-Id
        /// </summary>
        public string MacAddress { get; set; } = string.Empty;
        /// <summary>
        /// 设备ID mqtt连接建立时 在MqttClientId解析出 WebSocket请求参数 Client-Id
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;
        /// <summary>
        /// 最后活跃时间
        /// </summary>
        public DateTime LastActiveTime { get; set; } = DateTime.Now;

        // MQTT相关
        public string MqttClientId { get; set; } = string.Empty; // MQTT客户端ID
        public bool IsMqttConnected { get; set; } // MQTT连接状态

        public Session XiaoZhiSession { get; set; }
        // UDP相关
        public uint Ssrc { get; set; } // UDP音频包的SSRC（同步源标识符）
        public IPEndPoint UdpRemoteEndPoint { get; set; } // 设备UDP端点
        public string UdpAesKey { get; set; } // AES-CTR加密密钥（16进制）
        public byte[] UdpAesKeybyte
        { get
            {
                return Encoding.UTF8.GetBytes(UdpAesKey);
            } }
        public string UdpAesNonce { get; set; } // AES-CTR随机数（16进制）
        public byte[] UdpAesNoncebyte
        { get
            {
                return Encoding.UTF8.GetBytes(UdpAesNonce);
            } }
        public ushort LocalSequence { get; set; } // 发送端序列号（本地递增）
        public ushort RemoteSequence { get; set; } // 接收端序列号（验证连续性）
        public uint ExpectedSequence { get; set; } // 接收端预判的下一个序列号（期望值）
        public long LastSequenceCheckTime { get; set; } // 最后一次序列号校验时间

        // 依赖注入：日志、MQTT服务（单例）
        private readonly ILogger<MqttSession> _logger;
        private readonly MqttService _mqttService;
        // 会话存储（单例）
        private readonly MqttUdpSessionStore _sessionStore;
        private readonly HandlerManager _handlerManager;
        private readonly ProviderManager _providerManager;

        private readonly UdpAudioSender _udpAudioSender;
        /// <summary>
        /// 获取会话ID
        /// </summary>
        public string SessionId { get; set; }
        public XiaoZhiConfig _xiaoZhiConfig { get; set; }
        private readonly UdpBackgroundService _udpClient; // 复用全局UdpClient

        private readonly TokenSessionRegistry _tokenSessionRegistry;
        public MqttUdpSession(ILogger<MqttSession> logger, 
            MqttService mqttService, 
            MqttUdpSessionStore sessionStore,
            HandlerManager handlerManager, 
            ProviderManager providerManager,
            XiaoZhiConfig xiaoZhiConfig, 
            UdpBackgroundService udpBackgroundService,
            TokenSessionRegistry tokenSessionRegistry)
        {
            SessionId = RandomStringGenerator.GenerateRandomString(9);
            IsMqttConnected = true;
            UdpAesKey= AesKeyGenerator.GenerateAesKey(SessionId);
            UdpAesNonce = AesKeyGenerator.GenerateAesNonce(SessionId);
            Ssrc=RandomStringGenerator.GenerateUniqueSsrc();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mqttService = mqttService ?? throw new ArgumentNullException(nameof(mqttService));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            this._handlerManager = handlerManager;
            this._providerManager = providerManager;
            _xiaoZhiConfig = xiaoZhiConfig;
            _udpClient = udpBackgroundService;
            _udpAudioSender= new UdpAudioSender(Ssrc, UdpAesKey);
            _tokenSessionRegistry = tokenSessionRegistry;
        }
        public Task SetMqttClientId(string clientId)
        {
            this.MqttClientId = clientId;
            SetMacDeviceId(clientId);
            return Task.CompletedTask;
        }
        private const string Separator = "@@@";
        public Task SetMacDeviceId(string combinedStr)
        {
            // 按分隔符分割，只分割一次（保证第二个参数中包含分隔符也能正确解析）
            string[] parts = combinedStr.Split(Separator);
            if (parts.Length > 2)
            {
                MacAddress=parts[1];
                DeviceId=parts[2];
            }
            return  Task.CompletedTask;
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
        public void UpdateUdpRemoteEndPoint(IPEndPoint udpRemoteEndPoint)
        {
            if (this.UdpRemoteEndPoint != udpRemoteEndPoint)
            {
                this.UdpRemoteEndPoint = udpRemoteEndPoint;
            }
        }
        /// <summary>
        /// 刷新最后活动时间
        /// </summary>
        public void RefreshLastActivityTime()
        {
            this.LastActiveTime = DateTime.Now;
            if (this.XiaoZhiSession != null)
            { XiaoZhiSession.RefreshLastActivityTime();}
        }
        // 线程安全更新序列号
        private readonly object _sequenceLock = new object();
        public ushort IncrementLocalSequence()
        {
            lock (_sequenceLock)
            {
                return LocalSequence = (ushort)(LocalSequence + 1);
            }
        }

        // 验证接收端序列号（防重放+容错）
        public bool ValidateRemoteSequence(ushort receivedSequence)
        {
            lock (_sequenceLock)
            {
                LastSequenceCheckTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                // 防重放：拒绝小于期望值的序列号
                if (receivedSequence < RemoteSequence)
                {
                    return false;
                }
                // 容错：允许轻微跳跃（比如丢包），记录警告
                if (receivedSequence > RemoteSequence + 1)
                {
                    // 可接入日志系统，记录序列号跳跃
                    Console.WriteLine($"[WARNING] 设备{DeviceId} UDP序列号跳跃：期望{RemoteSequence}，实际{receivedSequence}");
                }
                RemoteSequence = receivedSequence;
                LastActiveTime = DateTime.Now;
                return true;
            }
        }

        public Task SendTtsMessageAsync(TtsStatus state, string? text = null)
        {
            if (this.XiaoZhiSession is null || this.XiaoZhiSession.PrivateProvider.Tts is null)
            {
                this._logger.LogError(Lang.SocketSession_SendTtsMessageAsync_SessionNotInitialized);
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
            return this.SendAsync(json, "tts");
        }

        public Task SendSttMessageAsync(string sttText)
        {
            if (this.XiaoZhiSession is null)
            {
                this._logger.LogError(Lang.SocketSession_SendSttMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new
            {
                Type = "stt",
                Text = sttText,
                SessionId = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(msg),"stt");
        }

        public Task SendLlmMessageAsync(Emotion emotion)
        {
            if (this.XiaoZhiSession is null)
            {
                this._logger.LogError(Lang.SocketSession_SendLlmMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var emo = new
            {
                Type = "llm",
                Text = emotion.GetDescription(),
                Emotion = emotion.GetName().ToLower(),
                SessionId = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(emo),"llm");
        }

        public Task SendAbortMessageAsync()
        {
            if (this.XiaoZhiSession is null)
            {
                this._logger.LogError(Lang.SocketSession_SendAbortMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var abortMessage = new
            {
                type = "tts",
                state = "stop",
                session_id = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(abortMessage),"abort");
        }

        public Task CloseSessionAsync(string reason = "")
        {
            //return this.DisconnectAsync(reason);
            UdpRemoteEndPoint = null;
            return Task.CompletedTask;
        }

        public async Task SendAsync(string json,string topic)
        {
            if(topic == "hello")
            {
                await SendHelloMessageAsync(json);
            }
            else
            {
                string to1 = GetTopic(topic);
                await OnMessageReceivedAsync(json, to1);
            }
            //_logger.LogInformation("MQTT Hello回复发送成功，MAC：{ClientId}，回复内容：{Topic}", MacAddress, json);

            //return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] bytePacket)
        {
            if (UdpRemoteEndPoint == null)
            {
                _logger.LogWarning("Session[{Ssrc}]的RemoteEndPoint为空，无法下发UDP消息", Ssrc);
            }
            else
            {
                await SendUdpAsync(bytePacket);
                //_logger.LogInformation("UDP服务端发消息，MAC：{ClientId}，长度：{Topic}", MacAddress, bytePacket.Length);
            }
        }

        public async Task SendUdpAsync(byte[] payload)
        {
            //byte[] udpjm= AesKeyGenerator.AesCtrEncrypt(payload, UdpAesKey, UdpAesNonce);
            //byte[] udpPacket = UdpAudioPacketBuilder.BuildUdpAudioPacket(udpjm, Ssrc);
            byte[] udpPacket = _udpAudioSender.BuildUdpPacket(payload);
            bool sendBytes = await _udpClient.SendUdpMessageAsync(UdpRemoteEndPoint, udpPacket);
            //if (!sendBytes)
            //{
            //    _logger.LogWarning(
            //        "Session[{Ssrc}]下发UDP消息失败，终端：{RemoteEP}",
            //        Ssrc, UdpRemoteEndPoint);
            //    return;
            //}
            //else
            //{
            //    _logger.LogInformation(
            //        "终端地址：[{Ssrc}]下发UDP消息成功，信息长度：{RemoteEP}",
            //        UdpRemoteEndPoint, udpPacket.Length);
            //}
        }
        /// <summary>
        /// MQTT客户端连接成功时触发（对标SocketSession.OnSessionConnectedAsync）
        /// </summary>
        /// <returns></returns>
        public async ValueTask OnConnectedAsync(MqttServer mqttServer,string clientId, EndPoint remoteEndPoint)
        {
            string to1 = $"device/{SessionId}/#";
            string to= Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(to1));
            //await mqttServer.SubscribeAsync(clientId, to);
            // 2. 构建 5.x 版本的 MqttTopicFilter 集合（关键）
            var topicFilters = new List<MqttTopicFilter>
            {
                // 单客户端专属主题：device/{SessionId}/#
                new MqttTopicFilter
                {
                    Topic = $"device/{SessionId}/#",
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
                    // 5.x 新增参数（按需设置，默认值即可）
                    NoLocal = false,
                    RetainAsPublished = false,
                    RetainHandling = MqttRetainHandling.SendAtSubscribe
                },
                // 群发主题：device/group/ALL
                new MqttTopicFilter
                {
                    Topic = "device/group/ALL",
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce
                }
            };

            // 3. 调用 5.x 的 SubscribeAsync 方法（参数和你贴的代码完全匹配）
            await mqttServer.SubscribeAsync(clientId, topicFilters);

            IPEndPoint userEndPoint = (remoteEndPoint as IPEndPoint)!;
            // 创建新的会话对象
            Session session = new Session(this.SessionId, MacAddress, string.Empty, userEndPoint,Session.ProtocolType.mqtt, this);
            session.DeviceToken = "AAAFPzL146bfSelCIxiGaYP73orWydK4ZOuDCajDn4bMPNXeIzYhp8y3ScGAQt0Xa";
            _tokenSessionRegistry.Register(session.DeviceToken, SessionId);

            // 初始化问候消息处理器
            this._handlerManager.InitializeHelloMessageHandler(session);
            // 刷新最后活动时间
            session.RefreshLastActivityTime();
            // 设置当前小知会话
            this.XiaoZhiSession = session;

            _logger.LogInformation("MQTT客户端连接成功，ClientId：{ClientId}，远程地址：{Endpoint}",
                clientId, remoteEndPoint);
            await Task.CompletedTask;
        }
        /// <summary>
        /// MQTT客户端断开连接时触发（对标SocketSession.OnSessionClosedAsync）
        /// </summary>
        /// <param name="reason">断开原因</param>
        /// <returns></returns>
        public async ValueTask OnDisconnectedAsync(string reason)
        {
            try
            {
                // 1. 日志记录：记录断开连接的关键信息（便于排查问题）
                _logger.LogInformation(
                    "MQTT客户端断开连接，DeviceId：{DeviceId}，MacAddress：{MacAddress}，ClientId：{MqttClientId}，原因：{Reason}",
                    DeviceId, MacAddress, MqttClientId, reason);

                // 2. 更新会话状态：标记MQTT连接为断开
                IsMqttConnected = false;
                LastActiveTime = DateTime.Now; // 记录最后断开时间

                // 3. 清理XiaoZhiSession（释放小知会话资源）
                if (XiaoZhiSession != null)
                {
                    XiaoZhiSession.Release();
                }

                _logger.LogDebug(
                    "MQTT会话清理完成，DeviceId：{DeviceId}，ClientId：{MqttClientId}",
                    DeviceId, MqttClientId);
            }
            catch (Exception ex)
            {
                // 异常兜底：保证断开流程不中断，仅记录日志
                _logger.LogError(
                    ex,
                    "处理MQTT客户端断开连接时发生异常，DeviceId：{DeviceId}，ClientId：{MqttClientId}，原因：{Reason}",
                    DeviceId, MqttClientId, reason);
            }
            finally
            {
                await Task.CompletedTask;
            }
        }
        /// <summary>
        /// UDP消息接收时触发
        /// </summary>
        /// <param name="payload">消息负载</param>
        /// <returns></returns>
        public async ValueTask OnUdpMessageReceivedAsync(byte[] payload)
        {
            try
            {

                _logger.LogInformation(
                    "UDP消息 长度：{DeviceId}",
                    payload.Length);
            }
            catch (Exception ex)
            {

            }
            await Task.CompletedTask;
        }
        /// <summary>
        /// 收到MQTT 消息时触发
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public async ValueTask OnMessageReceivedAsync(string text,string topic)
        {
            try
            {
                string top= topic;
                if (topic.Length == 0)
                {
                    top = GetTopic("Err");
                }
                string utext = AesKeyGenerator.ConvertUnicodeEscapeToUtf8String(text);
                //string replyJson = JsonConvert.SerializeObject(utext, Formatting.None);
                byte[] replyPayload = Encoding.UTF8.GetBytes(utext);
                // 5. 核心：调用IMqttProtocolEngine发布消息（完全适配你的架构）
                await _mqttService.PublishMessageAsync(
                    topic: top,
                    payload: replyPayload,
                    mqttNetQosLevel: MqttQualityOfServiceLevel.AtMostOnce,
                    retain: false,
                    cancellationToken: default);
                _logger.LogInformation("MQTT回复发送成功，MAC：{ClientId}，回复内容：{Topic}", MacAddress, utext);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "处理MQTT客户端收到消息时发生异常，Mac：{DeviceId}，ClientId：{MqttClientId}",
                    MacAddress, MqttClientId);
            }
            await Task.CompletedTask;
        }
        /// <summary>
        /// 收到MQTT Hello消息时触发
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public async ValueTask SendHelloMessageAsync(string text)
        {
            try
            {
                    var replyData = new
                    {
                        type = "hello",
                        transport = "udp",
                        session_id = SessionId,
                        audio_params = new
                        {
                            format = "opus",
                            sample_rate = 16000,
                            channels = 1,
                            frame_duration = 60
                        },
                        udp = new
                        {
                            server = _xiaoZhiConfig.UdpConfig.Server,
                            port = _xiaoZhiConfig.UdpConfig.Port,
                            key = UdpAesKey,
                            nonce = UdpAesNonce,
                            assigned_ssrc = Ssrc
                        }
                    };
                string replyJson = JsonConvert.SerializeObject(replyData, Formatting.None);
                byte[] replyPayload = Encoding.UTF8.GetBytes(replyJson);
                // 5. 核心：调用IMqttProtocolEngine发布消息（完全适配你的架构）
                await _mqttService.PublishMessageAsync(
                    topic: GetTopic("hello"),
                    payload: replyPayload,
                    mqttNetQosLevel: MqttQualityOfServiceLevel.AtMostOnce,
                    retain: false,
                    cancellationToken: default);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "处理MQTT客户端收到消息时发生异常，Mac：{DeviceId}，ClientId：{MqttClientId}",
                    MacAddress, MqttClientId);
            }
            await Task.CompletedTask;
        }
        public string GetTopic(string name)
        {
            return $"device/{SessionId}/{name}";
        }
        /// <summary>
        /// 主动断开当前客户端连接
        /// </summary>
        /// <param name="reason">断开原因</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task DisconnectAsync(string reason)
        {
            // 前置校验（匹配源码的ThrowIfNotStarted逻辑）
            if (!_mqttService.MqttServer.IsStarted)
            {
                throw new InvalidOperationException("MQTT服务端未启动");
            }

            if (string.IsNullOrEmpty(MqttClientId))
            {
                throw new ArgumentNullException(nameof(MqttClientId), "ClientId不能为空");
            }

            // 1. 构建断开选项（适配源码的MqttServerClientDisconnectOptions）
            var disconnectOptions = new MqttServerClientDisconnectOptions
            {
                ReasonCode = MqttDisconnectReasonCode.NormalDisconnection, // MQTT5原因码
                ReasonString = reason, // 断开原因描述
            };

            try
            {
                // 2. 调用源码中的DisconnectClientAsync方法
                await _mqttService.MqttServer.DisconnectClientAsync(MqttClientId, disconnectOptions);
                Console.WriteLine($"成功断开客户端 [{MqttClientId}] 连接，原因：{reason}");
            }
            catch (KeyNotFoundException)
            {
                Console.WriteLine($"客户端 [{MqttClientId}] 不存在或未连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开客户端 [{MqttClientId}] 失败：{ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
