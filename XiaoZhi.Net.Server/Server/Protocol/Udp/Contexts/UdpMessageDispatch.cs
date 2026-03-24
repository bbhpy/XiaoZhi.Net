using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP 消息分发类
    /// 职责：解密 UDP 数据包、校验序列号、将音频数据送入 Handler Pipeline
    /// </summary>
    internal class UdpMessageDispatch
    {
        private readonly ILogger<UdpMessageDispatch> _logger;

        public UdpMessageDispatch(ILogger<UdpMessageDispatch> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 分发 UDP 消息（由 Worker 调用）
        /// </summary>
        public async Task DispatchAsync(MqttUdpSession udpSession, byte[] udpPacket, CancellationToken cancellationToken = default)
        {
            try
            {
                if (udpSession == null || udpSession.XiaoZhiSession == null)
                {
                    _logger.LogWarning("UDP 会话未初始化，丢弃数据包");
                    return;
                }

                // 1. 解析 UDP 数据包
                if (!TryParseUdpPacket(udpPacket, out var packetInfo))
                {
                    _logger.LogWarning(
                        "UDP 数据包解析失败，SSRC={Ssrc}，远端={RemoteEP}",
                        udpSession.Ssrc, udpSession.UdpRemoteEndPoint);
                    return;
                }

                // 2. 校验 SSRC
                if (!ValidateSsrc(udpSession, packetInfo.Ssrc))
                {
                    _logger.LogWarning(
                        "UDP SSRC 不匹配，会话SSRC={SessionSsrc}，包SSRC={PacketSsrc}",
                        udpSession.Ssrc, packetInfo.Ssrc);
                    return;
                }

                // 3. AES-CTR 解密 Payload（使用与终端相同的 Nonce 构造方式）
                byte[] decryptedOpusData;
                try
                {
                    // 关键：从会话获取基础 Nonce
                    byte[] baseNonce = Convert.FromHexString(udpSession.UdpAesNonce);

                    // 从 UDP 包头中提取动态字段
                    ushort payloadLen = packetInfo.PayloadLen;
                    uint timestamp = packetInfo.Timestamp;
                    uint sequence = packetInfo.Sequence;

                    // 复制基础 Nonce
                    byte[] nonce = new byte[16];
                    Array.Copy(baseNonce, nonce, 16);

                    // 动态覆盖字段（与终端 mqtt_protocol.cc 第 105-108 行完全一致）
                    // 覆盖 offset 2-3: payload_len（大端）
                    nonce[2] = (byte)(payloadLen >> 8);
                    nonce[3] = (byte)(payloadLen & 0xFF);

                    // 覆盖 offset 8-11: timestamp（大端）
                    nonce[8] = (byte)(timestamp >> 24);
                    nonce[9] = (byte)(timestamp >> 16);
                    nonce[10] = (byte)(timestamp >> 8);
                    nonce[11] = (byte)(timestamp & 0xFF);

                    // 覆盖 offset 12-15: sequence（大端）
                    nonce[12] = (byte)(sequence >> 24);
                    nonce[13] = (byte)(sequence >> 16);
                    nonce[14] = (byte)(sequence >> 8);
                    nonce[15] = (byte)(sequence & 0xFF);

                    // 转换为十六进制字符串
                    string nonceHex = Convert.ToHexString(nonce);

                    decryptedOpusData = DecryptAesCtr(
                        packetInfo.Payload,
                        udpSession.UdpAesKey,
                        nonceHex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "UDP 数据包 AES-CTR 解密失败，设备ID={DeviceId}，序列号={Sequence}",
                        udpSession.XiaoZhiSession.DeviceId, packetInfo.Sequence);
                    return;
                }

                // 4. 序列号校验
                if (!ValidateSequence(udpSession, packetInfo.Sequence))
                {
                    _logger.LogWarning(
                        "UDP 序列号异常，设备ID={DeviceId}，收到={ReceivedSeq}，期望={ExpectedSeq}",
                        udpSession.XiaoZhiSession.DeviceId, packetInfo.Sequence, udpSession.ExpectedSequence);
                }

                // 5. 刷新会话活动时间
                udpSession.RefreshLastActivityTime();

                // 6. 交给业务处理器
                await udpSession.XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(decryptedOpusData);

                //_logger.LogDebug(
                //    "UDP 音频包处理成功，设备ID={DeviceId}，序列号={Sequence}，Opus长度={OpusLen}",
                //    udpSession.XiaoZhiSession.DeviceId, packetInfo.Sequence, decryptedOpusData.Length);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("UDP 消息分发已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP 消息分发异常");
            }
        }

        /// <summary>
        /// AES-CTR 解密（与终端 mbedtls_aes_crypt_ctr 完全对称）
        /// </summary>
        /// <param name="encryptedData">加密的数据（Payload）</param>
        /// <param name="aesKeyHex">AES 密钥（32 位十六进制字符串，16 字节）</param>
        /// <param name="nonceHex">Nonce（32 位十六进制字符串，16 字节），即 UDP 包头</param>
        /// <returns>解密后的 Opus 数据</returns>
        private byte[] DecryptAesCtr(byte[] encryptedData, string aesKeyHex, string nonceHex)
        {
            // 1. 将十六进制字符串转换为字节数组
            byte[] key = Convert.FromHexString(aesKeyHex);
            byte[] nonce = Convert.FromHexString(nonceHex);

            // 2. 校验长度（必须都是 16 字节）
            if (key.Length != 16)
            {
                throw new ArgumentException($"AES 密钥长度错误：预期 16 字节，实际 {key.Length} 字节", nameof(aesKeyHex));
            }
            if (nonce.Length != 16)
            {
                throw new ArgumentException($"Nonce 长度错误：预期 16 字节，实际 {nonce.Length} 字节", nameof(nonceHex));
            }

            // 3. CTR 模式下，解密 = 加密（XOR 相同的密钥流）
            byte[] decrypted = new byte[encryptedData.Length];

            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 128;      // 128 位 = 16 字节
                aes.BlockSize = 128;    // AES 块大小
                aes.Key = key;
                aes.Mode = CipherMode.ECB;      // CTR 模式内部使用 ECB 加密 Counter
                aes.Padding = PaddingMode.None;
                aes.IV = new byte[16];          // ECB 模式不需要 IV

                // 复制 Nonce 作为初始 Counter（与终端完全一致）
                byte[] counter = (byte[])nonce.Clone();
                byte[] keyStream = new byte[encryptedData.Length];
                int offset = 0;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    // 生成密钥流（与终端的 mbedtls_aes_crypt_ctr 逻辑一致）
                    while (offset < keyStream.Length)
                    {
                        // 加密 Counter 生成密钥块（每次 16 字节）
                        byte[] block = encryptor.TransformFinalBlock(counter, 0, counter.Length);
                        int copyLen = Math.Min(block.Length, keyStream.Length - offset);
                        Array.Copy(block, 0, keyStream, offset, copyLen);
                        offset += copyLen;

                        // 大端序递增 Counter（与终端 mbedtls_aes_crypt_ctr 的 Counter 递增逻辑一致）
                        IncrementCounterBigEndian(counter);
                    }
                }

                // CTR 模式：密文 XOR 密钥流 = 明文
                for (int i = 0; i < encryptedData.Length; i++)
                {
                    decrypted[i] = (byte)(encryptedData[i] ^ keyStream[i]);
                }
            }

            return decrypted;
        }

        /// <summary>
        /// 大端模式递增 Counter（与终端 mbedtls_aes_crypt_ctr 的 Counter 递增逻辑完全一致）
        /// </summary>
        private void IncrementCounterBigEndian(byte[] counter)
        {
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0)
                    break;
            }
        }

        /// <summary>
        /// 解析 UDP 数据包
        /// </summary>
        private bool TryParseUdpPacket(byte[] udpPacket, out UdpAudioPacketInfo packetInfo)
        {
            packetInfo = null;

            if (udpPacket == null || udpPacket.Length < 16)
                return false;

            byte type = udpPacket[0];
            if (type != 0x01)
                return false;

            try
            {
                ushort payloadLen = BinaryPrimitives.ReadUInt16BigEndian(udpPacket.AsSpan(2, 2));
                int expectedTotalLen = 16 + payloadLen;
                if (udpPacket.Length != expectedTotalLen)
                    return false;

                packetInfo = new UdpAudioPacketInfo
                {
                    Type = type,
                    Flags = udpPacket[1],
                    PayloadLen = payloadLen,
                    Ssrc = BinaryPrimitives.ReadUInt32BigEndian(udpPacket.AsSpan(4, 4)),
                    Timestamp = BinaryPrimitives.ReadUInt32BigEndian(udpPacket.AsSpan(8, 4)),
                    Sequence = BinaryPrimitives.ReadUInt32BigEndian(udpPacket.AsSpan(12, 4)),
                    Payload = udpPacket.AsSpan(16, payloadLen).ToArray()
                };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 校验序列号（防重放 + 连续性）
        /// </summary>
        private bool ValidateSequence(MqttUdpSession udpSession, uint receivedSequence)
        {
            // 首次接收，初始化期望值
            if (udpSession.ExpectedSequence == 0)
            {
                udpSession.ExpectedSequence = receivedSequence + 1;
                return true;
            }

            // 防重放：拒绝小于期望值的序列号
            if (receivedSequence < udpSession.ExpectedSequence)
            {
                _logger.LogWarning(
                    "UDP 序列号重放，设备ID={DeviceId}，收到={Received}，期望={Expected}",
                    udpSession.XiaoZhiSession.DeviceId, receivedSequence, udpSession.ExpectedSequence);
                return false;
            }

            // 连续性校验：允许轻微跳跃，超过阈值记录警告
            uint sequenceGap = receivedSequence - udpSession.ExpectedSequence;
            if (sequenceGap > 5)
            {
                _logger.LogWarning(
                    "UDP 序列号跳跃过大，设备ID={DeviceId}，收到={Received}，期望={Expected}，跳跃={Gap}",
                    udpSession.XiaoZhiSession.DeviceId, receivedSequence, udpSession.ExpectedSequence, sequenceGap);
            }

            // 更新期望值为当前序列号 + 1
            udpSession.ExpectedSequence = receivedSequence + 1;
            return true;
        }

        /// <summary>
        /// 校验 SSRC 是否与会话绑定的 SSRC 一致
        /// </summary>
        private bool ValidateSsrc(MqttUdpSession udpSession, uint packetSsrc)
        {
            return udpSession.Ssrc == packetSsrc;
        }

        /// <summary>
        /// UDP 音频数据包解析结果封装
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