using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp
{
    /// <summary>
    /// UDP音频包构建器（适配终端解密：Nonce=包头前16字节 + AES-CTR 1:1匹配mbedtls）
    /// </summary>
    internal class UdpAudioSender
    {
        // 终端固定参数（初始化后不变）
        private readonly uint _fixedSsrc;
        private readonly byte[] _fixedAesKey; // 16字节二进制密钥

        // 动态状态（线程安全，原子操作）
        private uint _sequence = 9; // 初始值=9 → 第一个包sequence=10（对齐终端日志）
        private uint _timestamp = 0; // 初始值=0 → 第一个包timestamp=60（60ms步长）

        // OPUS帧固定时间增量（60ms/帧，与终端一致）
        private const uint TimestampStep = 60;
        // AES块大小（固定16字节）
        private const int AesBlockSize = 16;

        /// <summary>
        /// 初始化UDP音频发送器（传入终端固定参数）
        /// </summary>
        /// <param name="ssrc">终端固定SSRC（如0x5664F3C6）</param>
        /// <param name="udpAesKeyHex">AES密钥（16进制字符串，32位）</param>
        /// <exception cref="ArgumentException">参数格式错误</exception>
        public UdpAudioSender(uint ssrc, string udpAesKeyHex)
        {
            // 校验固定参数
            if (string.IsNullOrEmpty(udpAesKeyHex) || udpAesKeyHex.Length != 32)
                throw new ArgumentException("AES密钥必须是32位16进制字符串", nameof(udpAesKeyHex));

            _fixedSsrc = ssrc;
            _fixedAesKey = HexToBytes(udpAesKeyHex);

            // 校验密钥长度（必须16字节）
            if (_fixedAesKey.Length != 16)
                throw new ArgumentException("AES密钥必须为16字节（32位16进制）");
        }

        /// <summary>
        /// 构建可直接发送的UDP音频包（完全匹配终端解密逻辑）
        /// </summary>
        /// <param name="rawOpusData">原始OPUS数据（16kHz/单声道/60ms帧长）</param>
        /// <returns>UDP包二进制数据（包头16字节 + 加密Payload）</returns>
        public byte[] BuildUdpPacket(byte[] rawOpusData)
        {
            // 1. 入参校验
            if (rawOpusData == null || rawOpusData.Length == 0)
                throw new ArgumentNullException(nameof(rawOpusData));
            if (rawOpusData.Length > ushort.MaxValue)
                throw new ArgumentException($"OPUS数据长度不能超过{ushort.MaxValue}字节", nameof(rawOpusData));

            // 2. 原子获取动态参数（线程安全，递增）
            uint sequence = Interlocked.Increment(ref _sequence);
            uint timestamp = Interlocked.Add(ref _timestamp, TimestampStep); // 修正：直接取递增后的值（第一个包=60）
            ushort rawOpusLen = (ushort)rawOpusData.Length;

            // 3. 先加密OPUS数据（临时用空Nonce，后续替换为真实包头）
            // 注：此处先加密，获取加密后长度，才能构造正确的包头
            byte[] encryptedPayload = AesCtrEncrypt(rawOpusData, _fixedAesKey, new byte[16]); // 临时Nonce不影响，仅占位

            // 4. 构造UDP包头（16字节，作为最终加密Nonce）
            byte[] header = new byte[16];
            var headerSpan = header.AsSpan();
            // 4.1 Type：1字节，固定0x01（匹配终端）
            headerSpan[0] = 0x01;
            // 4.2 Flags：1字节，固定0x00（匹配终端）
            headerSpan[1] = 0x00;
            // 4.3 PayloadLen：2字节，大端（加密后的Payload长度，关键！）
            BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(2, 2), (ushort)encryptedPayload.Length);
            // 4.4 SSRC：4字节，大端（终端固定SSRC，如0x5664F3C6）
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan.Slice(4, 4), _fixedSsrc);
            // 4.5 Timestamp：4字节，大端（动态值，60ms步长）
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan.Slice(8, 4), timestamp);
            // 4.6 Sequence：4字节，大端（动态自增）
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan.Slice(12, 4), sequence);

            // 5. 用真实包头作为Nonce，重新加密OPUS数据（核心修正！）
            encryptedPayload = AesCtrEncrypt(rawOpusData, _fixedAesKey, header);

            // 6. 拼接最终UDP包（包头 + 加密Payload）
            int totalLength = header.Length + encryptedPayload.Length;
            byte[] udpPacket = new byte[totalLength];
            Buffer.BlockCopy(header, 0, udpPacket, 0, header.Length);
            Buffer.BlockCopy(encryptedPayload, 0, udpPacket, header.Length, encryptedPayload.Length);

            return udpPacket;
        }
        /// <summary>
        /// 构建UDP音频包（支持自定义nonce和key，完全匹配终端解密逻辑）
        /// </summary>
        /// <param name="rawOpusData">原始OPUS数据（16kHz/单声道/60ms帧长）</param>
        /// <param name="ssrc">终端SSRC（从服务器获取）</param>
        /// <param name="aesKeyHex">AES密钥（16进制字符串，32位，从服务器获取）</param>
        /// <param name="baseNonceHex">基础Nonce（16进制字符串，32位，从服务器获取）</param>
        /// <returns>UDP包二进制数据（包头16字节 + 加密Payload）</returns>
        public byte[] BuildUdpPacket(byte[] rawOpusData, uint ssrc, string aesKeyHex, string baseNonceHex)
        {
            // 1. 参数校验
            if (rawOpusData == null || rawOpusData.Length == 0)
                throw new ArgumentNullException(nameof(rawOpusData));
            if (rawOpusData.Length > ushort.MaxValue)
                throw new ArgumentException($"OPUS数据长度不能超过{ushort.MaxValue}字节", nameof(rawOpusData));
            if (string.IsNullOrEmpty(aesKeyHex) || aesKeyHex.Length != 32)
                throw new ArgumentException("AES密钥必须是32位16进制字符串", nameof(aesKeyHex));
            if (string.IsNullOrEmpty(baseNonceHex) || baseNonceHex.Length != 32)
                throw new ArgumentException("Nonce必须是32位16进制字符串", nameof(baseNonceHex));

            // 2. 转换密钥和nonce
            byte[] aesKey = HexToBytes(aesKeyHex);
            byte[] baseNonce = HexToBytes(baseNonceHex);

            if (aesKey.Length != 16)
                throw new ArgumentException("AES密钥必须为16字节（32位16进制）");
            if (baseNonce.Length != 16)
                throw new ArgumentException("Nonce必须为16字节（32位16进制）");

            // 3. 动态参数（使用静态变量或传入，这里使用静态变量保持状态）
            // 注意：实际使用中需要维护 sequence 和 timestamp 的状态
            // 这里简化示例，实际应该作为类成员变量
            uint sequence = Interlocked.Increment(ref _sequence);
            uint timestamp = Interlocked.Add(ref _timestamp, TimestampStep); // 修正：直接取递增后的值（第一个包=60）
            ushort rawOpusLen = (ushort)rawOpusData.Length;

            // 4. 构造16字节UDP包头
            byte[] header = new byte[16];
            var headerSpan = header.AsSpan();

            // 4.1 Type: 1字节，固定0x01
            headerSpan[0] = 0x01;
            // 4.2 Flags: 1字节，固定0x00
            headerSpan[1] = 0x00;
            // 4.3 PayloadLen: 2字节，大端（原始OPUS数据长度，不是加密后的长度）
            BinaryPrimitives.WriteUInt16BigEndian(headerSpan.Slice(2, 2), rawOpusLen);
            // 4.4 SSRC: 4字节，大端（从服务器获取）
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan.Slice(4, 4), ssrc);
            // 4.5 Timestamp: 4字节，大端（动态值，60ms步长）
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan.Slice(8, 4), timestamp);
            // 4.6 Sequence: 4字节，大端（动态自增）
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan.Slice(12, 4), sequence);

            // 5. 构造Nonce（修改baseNonce的特定位置，完全匹配C++逻辑）
            byte[] nonce = (byte[])baseNonce.Clone();

            // 修改nonce[2-3]为payload_len（大端）
            byte[] payloadLenBytes = BitConverter.GetBytes(rawOpusLen);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(payloadLenBytes);
            Array.Copy(payloadLenBytes, 0, nonce, 2, 2);

            // 修改nonce[8-11]为timestamp（大端）
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timestampBytes);
            Array.Copy(timestampBytes, 0, nonce, 8, 4);

            // 修改nonce[12-15]为sequence（大端）
            byte[] sequenceBytes = BitConverter.GetBytes(sequence);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(sequenceBytes);
            Array.Copy(sequenceBytes, 0, nonce, 12, 4);

            // 6. 用修改后的nonce加密OPUS数据
            byte[] encryptedPayload = AesCtrEncrypt(rawOpusData, aesKey, nonce);

            // 7. 拼接最终UDP包（包头 + 加密Payload）
            int totalLength = header.Length + encryptedPayload.Length;
            byte[] udpPacket = new byte[totalLength];
            Buffer.BlockCopy(header, 0, udpPacket, 0, header.Length);
            Buffer.BlockCopy(encryptedPayload, 0, udpPacket, header.Length, encryptedPayload.Length);

            return udpPacket;
        }

        /// <summary>
        /// AES-CTR 128位加密（与终端mbedtls_aes_crypt_ctr逻辑1:1匹配）
        /// </summary>
        private byte[] AesCtrEncrypt(byte[] input, byte[] key, byte[] nonce)
        {
            byte[] output = new byte[input.Length];
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.BlockSize = 128;
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.IV = new byte[16]; // ECB模式IV无意义，设为空

                // 初始化Counter = Nonce（终端逻辑：Counter初始值=包头前16字节）
                byte[] counter = (byte[])nonce.Clone();
                byte[] keyStream = new byte[input.Length];
                int offset = 0;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    // 生成密钥流（与mbedtls逻辑一致）
                    while (offset < keyStream.Length)
                    {
                        // 加密Counter生成密钥块
                        byte[] block = encryptor.TransformFinalBlock(counter, 0, counter.Length);
                        int copyLen = Math.Min(block.Length, keyStream.Length - offset);
                        Array.Copy(block, 0, keyStream, offset, copyLen);
                        offset += copyLen;

                        // 大端序递增Counter（完全匹配mbedtls_aes_crypt_ctr）
                        IncrementCounterBigEndian(counter);
                    }
                }

                // CTR模式核心：明文 XOR 密钥流 = 密文（终端解密时反向XOR）
                for (int i = 0; i < input.Length; i++)
                {
                    output[i] = (byte)(input[i] ^ keyStream[i]);
                }
            }
            return output;
        }

        /// <summary>
        /// 大端模式递增Counter（与mbedtls_counter_inc逻辑1:1）
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
        /// 16进制字符串转字节数组（与终端DecodeHexString逻辑一致）
        /// </summary>
        private byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// 重置动态状态（sequence/timestamp）
        /// </summary>
        public void ResetState()
        {
            Interlocked.Exchange(ref _sequence, 9); // 重置后第一个包sequence=10
            Interlocked.Exchange(ref _timestamp, 0);
        }
    }
}