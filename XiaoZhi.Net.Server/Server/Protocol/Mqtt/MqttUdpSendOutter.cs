using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt
{
    /// <summary>
    /// MQTT+UDP 合并的发送实现类
    /// 适配IBizSendOutter，内部区分MQTT/UDP的发送逻辑
    /// </summary>
    internal class MqttUdpSendOutter : IBizSendOutter
    {
        // 依赖的核心组件
        private readonly MqttUdpSession? _session;
        private readonly IMqttClient _mqttClient; // MQTT客户端（需提前注册到DI）
        private readonly UdpClient _udpClient;    
        private readonly ILogger<MqttUdpSendOutter> _logger;


        /// <summary>
        /// 构造函数：注入依赖，初始化加密上下文
        /// </summary>
        public MqttUdpSendOutter(
            MqttUdpSession? session,
            IMqttClient mqttClient,
            UdpClient senderUdpClient,
            ILogger<MqttUdpSendOutter> logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient)); 
            _udpClient = senderUdpClient ?? throw new ArgumentNullException(nameof(senderUdpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        }

        #region IBizSendOutter 接口实现
        /// <summary>
        /// 会话ID（直接复用合并会话的SessionId）
        /// </summary>
        public string SessionId => _session.SessionId;

        /// <summary>
        /// 获取会话对象（返回合并会话）
        /// </summary>
        public Session GetSession()
        {
            // 若原有Session是抽象类/基类，需将MqttUdpCompositeSession适配为Session
            // 示例：如果Session是WebSocketSession的基类，可封装返回；若只是标识接口，直接返回_session
            return _session.XiaoZhiSession as Session ?? throw new InvalidOperationException("合并会话无法转换为基础Session类型");
        }

        /// <summary>
        /// 发送语音合成消息（控制类消息走MQTT，音频数据走UDP）
        /// </summary>
        public async Task SendTtsMessageAsync(TtsStatus state, string? text = null)
        {
            try
            {
                // 1. TTS状态/文本等控制信息走MQTT发送
                var ttsMsg = new
                {
                    type = "tts",
                    session_id = _session.SessionId,
                    device_id = _session.DeviceId,
                    state = state.ToString(),
                    text = text
                };
                string mqttTopic = $"device/{_session.DeviceId}/tts"; // 按业务定义Topic
                await SendMqttMessageAsync(mqttTopic, ttsMsg);

                // 2. 若有TTS音频数据（比如合成后的Opus流），走UDP加密发送
                // 示例：if (audioData != null) await SendUdpAudioPacketAsync(audioData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送TTS消息失败：SessionId={SessionId}", _session.SessionId);
                throw;
            }
        }

        /// <summary>
        /// 发送即时语音转文字消息（走MQTT）
        /// </summary>
        public async Task SendSttMessageAsync(string sttText)
        {
            try
            {
                var sttMsg = new
                {
                    type = "stt",
                    session_id = _session.SessionId,
                    device_id = _session.DeviceId,
                    text = sttText
                };
                string mqttTopic = $"device/{_session.DeviceId}/stt";
                await SendMqttMessageAsync(mqttTopic, sttMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送STT消息失败：SessionId={SessionId}", _session.SessionId);
                throw;
            }
        }

        /// <summary>
        /// 发送大型语言模型消息（走MQTT）
        /// </summary>
        public async Task SendLlmMessageAsync(Emotion emotion)
        {
            try
            {
                var llmMsg = new
                {
                    type = "llm",
                    session_id = _session.SessionId,
                    device_id = _session.DeviceId,
                    emotion = emotion.ToString()
                };
                string mqttTopic = $"device/{_session.DeviceId}/llm";
                await SendMqttMessageAsync(mqttTopic, llmMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送LLM消息失败：SessionId={SessionId}", _session.SessionId);
                throw;
            }
        }

        /// <summary>
        /// 发送中止消息（MQTT+UDP双端通知）
        /// </summary>
        public async Task SendAbortMessageAsync()
        {
            try
            {
                // 1. MQTT发送中止控制消息
                var abortMsg = new
                {
                    type = "abort",
                    session_id = _session.SessionId,
                    device_id = _session.DeviceId
                };
                await SendMqttMessageAsync($"device/{_session.DeviceId}/control", abortMsg);

                // 2. UDP发送中止数据包（可选，确保设备及时停止音频传输）
                byte[] abortPacket = BuildUdpControlPacket(0x02); // type=0x02表示中止
                await SendAsync(abortPacket);

                _logger.LogInformation("发送中止消息：SessionId={SessionId}", _session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送中止消息失败：SessionId={SessionId}", _session.SessionId);
                throw;
            }
        }

        /// <summary>
        /// 关闭会话（清理MQTT订阅+UDP会话）
        /// </summary>
        public async Task CloseSessionAsync(string reason = "")
        {
            try
            {
                // 1. MQTT取消订阅
                string[] topics = new[] {
                $"device/{_session.DeviceId}/tts",
                $"device/{_session.DeviceId}/stt",
                $"device/{_session.DeviceId}/llm"
            }; 
            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
            .WithTopicFilter(topics[0])
            .WithTopicFilter(topics[1])
            .WithTopicFilter(topics[2])
            .Build();
                await _mqttClient.UnsubscribeAsync(unsubscribeOptions);

                // 2. 标记会话状态
                _session.IsMqttConnected = false;
                _session.LastActiveTime = DateTime.Now;

                // 3. 记录关闭日志
                _logger.LogInformation("关闭会话：SessionId={SessionId}，原因：{Reason}", _session.SessionId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭会话失败：SessionId={SessionId}", _session.SessionId);
                throw;
            }
        }
        #endregion

        #region ISocketSendOutter 接口实现
        /// <summary>
        /// 发送JSON（默认走MQTT）
        /// </summary>
        public async Task SendAsync(string json,string topic)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            // JSON消息默认走MQTT（控制类消息）
            string defaultTopic = $"device/{_session?.DeviceId}/control";
            // 构造应用消息
            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(defaultTopic)
                .WithPayload(json)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(applicationMessage);
            _logger.LogDebug("MQTT发送JSON：SessionId={SessionId}，Topic={Topic}，内容={Json}",
                _session?.SessionId, defaultTopic, json);
        }

        /// <summary>
        /// 发送字节包（区分UDP/MQTT：音频包走UDP，其他走MQTT）
        /// </summary>
        public async Task SendAsync(byte[] bytePacket)
        {
            if (bytePacket == null || bytePacket.Length == 0)
                throw new ArgumentNullException(nameof(bytePacket));

            byte packetType = bytePacket[0];
            if (packetType == 0x01 && _session?.UdpRemoteEndPoint != null)
            {
                // 音频包：使用发送专用客户端走UDP
                byte[] encryptedPacket = EncryptUdpAudioPacket(bytePacket);
                await _udpClient.SendAsync(encryptedPacket, encryptedPacket.Length, _session.UdpRemoteEndPoint);
                _logger.LogDebug("UDP发送加密音频包：SessionId={SessionId}，长度={Length}，SSRC={SSRC}",
                    _session.SessionId, encryptedPacket.Length, _session.Ssrc);
            }
            else
            {
                // 非音频包：走MQTT
                string topic = $"device/{_session?.DeviceId}/data";
                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(bytePacket)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();
                await _mqttClient.PublishAsync(applicationMessage);
                _logger.LogDebug("MQTT发送字节包：SessionId={SessionId}，Topic={Topic}，长度={Length}",
                    _session?.SessionId, topic, bytePacket.Length);
            }
        }
        #endregion

        #region 私有辅助方法
        /// <summary>
        /// MQTT消息发送通用方法（序列化+发布）
        /// </summary>
        private async Task SendMqttMessageAsync(string topic, object message)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(message);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await _mqttClient.PublishAsync(applicationMessage);
            _logger.LogDebug("MQTT发送消息：SessionId={SessionId}，Topic={Topic}，内容={Json}",
                _session.SessionId, topic, json);
        }

        /// <summary>
        /// UDP音频包加密（适配AES-CTR）
        /// </summary>
        private byte[] EncryptUdpAudioPacket(byte[] rawPacket)
        {
            // 1. 解析原始包结构（type+flags+payload_len+ssrc+timestamp+sequence+payload）
            // 2. 加密payload部分
            // 3. 重新组装数据包（保持头部不变，替换加密后的payload）
            // 示例逻辑（需按你定义的包结构调整）：
            int payloadOffset = 1 + 1 + 2 + 4 + 4 + 4; // type(1)+flags(1)+payload_len(2)+ssrc(4)+timestamp(4)+sequence(4)
            byte[] payload = rawPacket.Skip(payloadOffset).ToArray();
            byte[] encryptedPayload = payload;//AesKeyGenerator.Encrypt(payload);

            // 更新payload_len（网络字节序）
            byte[] payloadLenBytes = BitConverter.GetBytes((ushort)encryptedPayload.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(payloadLenBytes); // 转网络字节序

            // 组装最终数据包
            using (var ms = new MemoryStream())
            {
                ms.Write(rawPacket, 0, payloadOffset); // 写入头部
                ms.Write(encryptedPayload, 0, encryptedPayload.Length); // 写入加密payload
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 构建UDP控制包（如中止、心跳）
        /// </summary>
        private byte[] BuildUdpControlPacket(byte type)
        {
            // 按UDP包结构组装控制包（payload_len=0）
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(type); // type
                ms.WriteByte(0x00); // flags
                ms.Write(BitConverter.GetBytes((ushort)0), 0, 2); // payload_len=0
                ms.Write(BitConverter.GetBytes(_session.Ssrc), 0, 4); // ssrc
                ms.Write(BitConverter.GetBytes(0), 0, 4); // timestamp=0
                ms.Write(BitConverter.GetBytes(_session.IncrementLocalSequence()), 0, 4); // sequence
                return ms.ToArray();
            }
        }
        #endregion
    }
}
