using Serilog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP消息分发类（对标MessageDispatch）
    /// 适配UDP协议：解析二进制数据包，路由到音频处理器，复用WebSocket的二进制处理逻辑
    /// </summary>
    internal class UdpMessageDispatch
    {
        /// <summary>
        /// 日志记录器（复用WebSocket的Serilog配置，统一日志格式）
        /// </summary>
        private static readonly ILogger _logger = Log.ForContext<UdpMessageDispatch>();

        /// <summary>
        /// 分发UDP二进制消息（对标MessageDispatch.DispatchAsync，UDP同步处理）
        /// 核心：按指定格式解析包、解密、校验序列号，最终交给业务处理器
        /// </summary>
        /// <param name="udpSession">UDP逻辑会话（绑定了XiaoZhiSession）</param>
        /// <param name="udpPacket">UDP二进制数据包（Opus音频帧）</param>
        /// <returns>异步任务（适配业务层异步逻辑）</returns>
        public static async Task DispatchAsync(MqttUdpSession udpSession, byte[] udpPacket)
        {
            try
            {
                // 前置校验：会话和数据包合法性
                if (udpSession == null || udpSession.XiaoZhiSession == null)
                {
                    _logger.Warning("UDP会话未初始化，丢弃数据包（远端IP：{RemoteIp}）",
                        udpSession.UdpRemoteEndPoint?.ToString());
                    return;
                }
                if (!ValidateUdpPacketFormat(udpPacket))
                {
                    _logger.Error("UDP数据包格式非法，长度：{Length}，远端IP：{RemoteIp}",
                        udpPacket.Length, udpSession.UdpRemoteEndPoint?.ToString());
                    return;
                }

                // 步骤1：解析UDP数据包固定格式（按字节偏移解析）
                var packetParseResult = ParseUdpAudioPacket(udpPacket);
                if (packetParseResult == null)
                {
                    _logger.Error("UDP数据包解析失败，远端IP：{RemoteIp}，设备ID：{DeviceId}",
                        udpSession.UdpRemoteEndPoint?.ToString(), udpSession.XiaoZhiSession.DeviceId);
                    return;
                }

                if (!ValidateSsrc(udpSession, packetParseResult.Ssrc))
                {
                    _logger.Error("UDP SSRC不匹配，丢弃数据包！设备ID：{DeviceId}，会话SSRC：{SessionSsrc}，包SSRC：{PacketSsrc}",
                        udpSession.XiaoZhiSession.DeviceId, udpSession.Ssrc, packetParseResult.Ssrc);
                    return; // 直接返回，不执行后续解密
                }

                // 步骤2：AES-GCM解密payload（加密的Opus数据，含16字节Tag）
                byte[] decryptedOpusData;
                try
                {
                    // 构建关联数据AAD：timestamp(4字节) + sequence(4字节)，增强完整性校验
                    byte[] aad = new byte[8];
                    BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(0, 4), packetParseResult.Timestamp);
                    BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(4, 4), packetParseResult.Sequence);

                    decryptedOpusData = DecryptAesGcm(
                        packetParseResult.Payload,
                        udpSession.UdpAesKey,
                        udpSession.UdpAesNonce,
                        aad); // 传递timestamp+sequence作为AAD
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "UDP数据包AES-GCM解密失败，设备ID：{DeviceId}，序列号：{Sequence}",
                        udpSession.XiaoZhiSession.DeviceId, packetParseResult.Sequence);
                    return;
                }

                // 步骤3：序列号校验（连续性、防重放）
                if (!ValidateSequence(udpSession, packetParseResult.Sequence))
                {
                    // 仅记录警告，仍处理数据包（容错处理）
                    _logger.Warning("UDP序列号异常，设备ID：{DeviceId}，收到：{ReceivedSeq}",
                        udpSession.XiaoZhiSession.DeviceId, packetParseResult.Sequence);
                }

                // 步骤4：刷新会话活动时间（防止超时）
                udpSession.RefreshLastActivityTime();

                // 步骤5：交给业务处理器（复用WebSocket的二进制处理逻辑）
                await udpSession.XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(decryptedOpusData);

                _logger.Debug("UDP音频包处理成功，设备ID：{DeviceId}，SSRC：{Ssrc}，序列号：{Sequence}，Opus长度：{OpusLen}",
                    udpSession.XiaoZhiSession.DeviceId, packetParseResult.Ssrc, packetParseResult.Sequence, decryptedOpusData.Length);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "UDP消息分发异常，远端IP：{RemoteIp}，设备ID：{DeviceId}",
                    udpSession.UdpRemoteEndPoint?.ToString(), udpSession.XiaoZhiSession?.DeviceId ?? "未知");
            }
        }

        /// <summary>
        /// 校验UDP数据包格式（自定义帧格式：type(1)+flags(1)+payload_len(2)+ssrc(4)+timestamp(4)+sequence(4)+payload）
        /// 适配UDP无连接特性：防止非法/残缺数据包进入业务层
        /// </summary>
        /// <param name="udpPacket">UDP数据包</param>
        /// <returns>true=格式合法，false=格式非法</returns>
        private static bool ValidateUdpPacketFormat(byte[] udpPacket)
        {
            // 最小包长度：1+1+2+4+4+4 = 16字节（无payload）
            if (udpPacket == null || udpPacket.Length < 16)
                return false;

            // 校验type：固定为0x01
            byte type = udpPacket[0];
            if (type != 0x01)
                return false;

            // 校验payload_len：网络字节序（大端），且实际长度匹配
            ushort payloadLen = BinaryPrimitives.ReadUInt16BigEndian(udpPacket.AsSpan(2, 2));
            int expectedTotalLen = 16 + payloadLen; // 16字节头 + payload长度
            if (udpPacket.Length != expectedTotalLen)
                return false;

            return true;
        }

        /// <summary>
        /// 解析UDP音频数据包（按指定格式提取各字段）
        /// </summary>
        /// <param name="udpPacket">完整UDP数据包</param>
        /// <returns>解析结果（null=解析失败）</returns>
        private static UdpAudioPacketInfo? ParseUdpAudioPacket(byte[] udpPacket)
        {
            try
            {
                return new UdpAudioPacketInfo
                {
                    Type = udpPacket[0], // 固定0x01
                    Flags = udpPacket[1],
                    PayloadLen = BinaryPrimitives.ReadUInt16BigEndian(udpPacket.AsSpan(2, 2)),
                    Ssrc = BinaryPrimitives.ReadUInt32BigEndian(udpPacket.AsSpan(4, 4)),
                    Timestamp = BinaryPrimitives.ReadUInt32BigEndian(udpPacket.AsSpan(8, 4)),
                    Sequence = BinaryPrimitives.ReadUInt32BigEndian(udpPacket.AsSpan(12, 4)),
                    Payload = udpPacket.AsSpan(16, (int)BinaryPrimitives.ReadUInt16BigEndian(udpPacket.AsSpan(2, 2))).ToArray()
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// AES-GCM模式解密Opus音频数据（UDP包约定：Payload最后16字节为GCM Tag）
        /// 核心特性：自带完整性校验，篡改/丢失Tag会直接抛异常，无需额外防篡改逻辑
        /// </summary>
        /// <param name="encryptedPayload">加密的Payload（= 密文 + 16字节Tag）</param>
        /// <param name="aesKeyHex">128位密钥（十六进制字符串，来自DeviceId）</param>
        /// <param name="aesNonceHex">128位随机数（十六进制字符串，来自SessionId）</param>
        /// <param name="associatedData">关联数据（AAD，可选，推荐传入timestamp+sequence增强安全性）</param>
        /// <returns>解密后的Opus原始音频数据</returns>
        /// <exception cref="CryptographicException">解密失败（Tag校验失败/数据篡改/格式错误）</exception>
        private static byte[] DecryptAesGcm(byte[] encryptedPayload, string aesKeyHex, string aesNonceHex, byte[] associatedData = null)
        {
            try
            {
                // 1. 转换十六进制密钥/nonce为字节数组（AES-GCM密钥支持128/192/256位，此处用128位）
                byte[] key = Convert.FromHexString(aesKeyHex);
                byte[] nonce = Convert.FromHexString(aesNonceHex);

                // 2. 校验nonce长度（AES-GCM推荐96位=12字节，兼容16字节但需提示）
                if (nonce.Length != 12)
                {
                    _logger.Warning("AES-GCM Nonce长度为{Len}字节（推荐12字节），可能影响安全性", nonce.Length);
                }

                // 3. 拆分密文和Tag（UDP包格式约定：最后16字节为GCM Tag）
                int tagSize = 16; // AES-GCM标准Tag大小（12/13/14/15/16字节，此处固定16）
                if (encryptedPayload.Length < tagSize)
                    throw new CryptographicException($"加密Payload长度{encryptedPayload.Length}不足，无法拆分{tagSize}字节Tag");

                // 拆分：前N-16字节=密文，最后16字节=Tag
                byte[] ciphertext = encryptedPayload.AsSpan(0, encryptedPayload.Length - tagSize).ToArray();
                byte[] tag = encryptedPayload.AsSpan(encryptedPayload.Length - tagSize).ToArray();

                // 4. 初始化AES-GCM（.NET 8+ 显式指定Tag大小，解决过时警告）
                byte[] plaintext = new byte[ciphertext.Length];
                using var aesGcm = new AesGcm(key, tagSize); // 显式指定Tag大小，与拆分逻辑一致

                // 5. 执行解密（自带完整性校验：Tag不匹配/数据篡改会直接抛CryptographicException）
                // associatedData：可选，传入timestamp+sequence可防止重放/篡改这些字段
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

                _logger.Debug("AES-GCM解密成功，密文长度：{CipherLen}，Tag长度：{TagLen}，解密后Opus长度：{PlainLen}",
                    ciphertext.Length, tag.Length, plaintext.Length);

                return plaintext;
            }
            catch (CryptographicException ex)
            {
                _logger.Error(ex, "AES-GCM解密失败（Tag校验/数据篡改/格式错误）");
                throw; // 抛上层处理，保证非法包被丢弃
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "AES-GCM解密过程异常");
                throw new CryptographicException("AES-GCM解密异常", ex); // 统一异常类型，方便上层捕获
            }
        }

        /// <summary>
        /// 校验序列号（连续性、防重放）
        /// </summary>
        /// <param name="udpSession">UDP会话</param>
        /// <param name="receivedSequence">收到的序列号（网络字节序转本地后的值）</param>
        /// <returns>true=序列号正常（允许轻微跳跃），false=异常（重放/过期）</returns>
        private static bool ValidateSequence(MqttUdpSession udpSession, uint receivedSequence)
        {
            // 1. 初始化期望值：如果是第一次接收，先把期望值设为收到的序列号+1
            if (udpSession.RemoteSequence == 0) // 假设初始值为0表示未接收过包
            {
                udpSession.RemoteSequence = (ushort)receivedSequence; // 记录实际收到的最新序列号
                udpSession.ExpectedSequence = receivedSequence + 1;   // 预判下一个该收的
                return true;
            }

            // 2. 防重放校验：拒绝小于期望值的序列号（旧包/重发包）
            if (receivedSequence < udpSession.ExpectedSequence)
            {
                _logger.Warning("UDP序列号异常（重放/过期），设备ID：{DeviceId}，收到：{Received}，期望：{Expected}",
                    udpSession.XiaoZhiSession.DeviceId, receivedSequence, udpSession.ExpectedSequence);
                return false;
            }

            // 3. 连续性校验：允许轻微跳跃（≤2个），超过则警告
            uint sequenceGap = receivedSequence - udpSession.ExpectedSequence;
            bool isNormal = sequenceGap <= 2;
            if (!isNormal)
            {
                _logger.Warning("UDP序列号跳跃过大，设备ID：{DeviceId}，收到：{Received}，期望：{Expected}，跳跃值：{Gap}",
                    udpSession.XiaoZhiSession.DeviceId, receivedSequence, udpSession.ExpectedSequence, sequenceGap);
            }

            // 4. 更新状态：无论是否轻微跳跃，都更新实际收到的序列号和下一个期望值
            udpSession.RemoteSequence = (ushort)receivedSequence; // 记录最新收到的
            udpSession.ExpectedSequence = receivedSequence + 1;    // 预判下一个该收的

            // 5. 返回结果：轻微跳跃也算正常（仅警告），只有重放/过期才返回false
            return isNormal;
        }
        /// <summary>
        /// 校验UDP包中的SSRC是否与会话绑定的SSRC一致
        /// 不一致则直接丢弃数据包，无需解密
        /// </summary>
        /// <param name="udpSession">UDP会话（存储会话专属SSRC）</param>
        /// <param name="packetSsrc">UDP包中解析出的SSRC</param>
        /// <returns>true=匹配，false=不匹配</returns>
        private static bool ValidateSsrc(MqttUdpSession udpSession, uint packetSsrc)
        {
            // 核心逻辑：仅需对比会话SSRC和包中SSRC是否相等
            return udpSession.Ssrc == packetSsrc;
        }

        /// <summary>
        /// UDP音频数据包解析结果封装
        /// </summary>
        private class UdpAudioPacketInfo
        {
            public byte Type { get; set; }
            public byte Flags { get; set; }
            public ushort PayloadLen { get; set; }
            public uint Ssrc { get; set; }
            public uint Timestamp { get; set; }
            public uint Sequence { get; set; }
            public byte[] Payload { get; set; } = Array.Empty<byte>();
        }
    }
}
