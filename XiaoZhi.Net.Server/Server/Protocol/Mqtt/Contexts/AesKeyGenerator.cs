using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts
{
    /// <summary>
    /// AES密钥生成工具类 udp key和nonce生成逻辑
    /// </summary>
    internal static class AesKeyGenerator
    {
        private const string DefaultKey = "rpdv3GvtGMe&#FkZ"; // 16位示例密钥
                                                                 
        private const string DefaultIV = "Be4Zkc%7yWet.5GC";  // 16位示例向量

        /// <summary>
        /// 根据传入的字符串生成 AES 加密密钥（16字节/128位，十六进制字符串）
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>32位十六进制字符串（对应16字节）</returns>
        /// <exception cref="ArgumentNullException">输入字符串为空时抛出</exception>
        public static string GenerateAesKey(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new ArgumentNullException(nameof(input), "输入字符串不能为空");
            }

            // 加入盐值区分key和nonce，避免相同输入生成相同结果
            return GenerateFixedSizeHexString(input, "udp.key,tghis.v$");
        }

        /// <summary>
        /// 根据传入的字符串生成 AES 加密随机数（16字节/128位，十六进制字符串）
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>32位十六进制字符串（对应16字节）</returns>
        /// <exception cref="ArgumentNullException">输入字符串为空时抛出</exception>
        public static string GenerateAesNonce(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new ArgumentNullException(nameof(input), "输入字符串不能为空");
            }

            // 不同的盐值，确保key和nonce结果不同
            return GenerateFixedSizeHexString(input, "mqtt.nonce;tghis.#1");
        }

        /// <summary>
        /// 核心生成方法：输入字符串 + 盐值 → 哈希 → 截取16字节 → 转十六进制
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <param name="salt">盐值</param>
        /// <returns>32位十六进制字符串</returns>
        private static string GenerateFixedSizeHexString(string input, string salt)
        {
            // 组合输入和盐值，增强唯一性
            var combinedBytes = Encoding.UTF8.GetBytes(input + salt);

            // 使用SHA256哈希，确保输出长度足够（32字节）
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(combinedBytes);

                // 截取前16字节（满足128位要求）
                var keyBytes = new byte[16];
                Array.Copy(hashBytes, 0, keyBytes, 0, 16);

                // 转换为十六进制字符串（32位）
                return BitConverter.ToString(keyBytes).Replace("-", "").ToLowerInvariant();
            }
        }
        // AES-CTR 解密核心方法
        /// <summary>
        /// 解密UDP音频Payload
        /// </summary>
        /// <param name="encryptedPayload">加密后的Payload数据</param>
        /// <param name="baseNonceHex">服务端下发的基础nonce（16进制字符串）</param>
        /// <param name="payloadLen">包头中的payload_len（网络字节序）</param>
        /// <param name="timestamp">包头中的timestamp（网络字节序）</param>
        /// <param name="sequence">包头中的sequence（网络字节序）</param>
        /// <param name="aesKeyHex">服务端下发的AES密钥（16进制字符串）</param>
        /// <returns>解密后的OPUS音频数据</returns>
        public static byte[] DecryptUdpAudioPayload(
            byte[] encryptedPayload,
            string baseNonceHex,
            ushort payloadLen,
            uint timestamp,
            uint sequence,
            string aesKeyHex)
        {
            if (encryptedPayload == null || encryptedPayload.Length == 0)
                throw new ArgumentNullException(nameof(encryptedPayload));

            // 1. 解析16进制的密钥和基础Nonce
            byte[] aesKey = HexStringToBytes(aesKeyHex);
            byte[] nonce = HexStringToBytes(baseNonceHex);

            // 2. 动态替换Nonce中的字段（和ESP32端完全一致）
            // nonce[2-3] = payload_len (2字节，网络字节序)
            byte[] payloadLenBytes = BitConverter.GetBytes(payloadLen);
            if (BitConverter.IsLittleEndian) Array.Reverse(payloadLenBytes); // 转为大端
            Array.Copy(payloadLenBytes, 0, nonce, 2, 2);

            // nonce[8-11] = timestamp (4字节，网络字节序)
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            if (BitConverter.IsLittleEndian) Array.Reverse(timestampBytes); // 转为大端
            Array.Copy(timestampBytes, 0, nonce, 8, 4);

            // nonce[12-15] = sequence (4字节，网络字节序)
            byte[] sequenceBytes = BitConverter.GetBytes(sequence);
            if (BitConverter.IsLittleEndian) Array.Reverse(sequenceBytes); // 转为大端
            Array.Copy(sequenceBytes, 0, nonce, 12, 4);

            // 3. AES-CTR 解密（.NET中CTR模式需手动实现或使用BouncyCastle，这里提供两种方案）
            // 方案1：使用.NET内置Aes（需注意CTR模式的IV处理）
            return DecryptAesCtr(encryptedPayload, aesKey, nonce);

            // 方案2：使用BouncyCastle（更简洁，需安装NuGet包：Install-Package BouncyCastle）
            // return DecryptAesCtrWithBouncyCastle(encryptedPayload, aesKey, nonce);
        } 
        // .NET内置AES-CTR解密实现（核心）
        private static byte[] DecryptAesCtr(byte[] input, byte[] key, byte[] nonce)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.BlockSize = 128;
                aes.Key = key;
                aes.Mode = CipherMode.ECB; // CTR模式基于ECB实现
                aes.Padding = PaddingMode.None; // CTR无需padding

                // CTR模式：nonce(16字节) = counter(前8字节) + nonce(后8字节)
                // 这里直接使用完整nonce作为IV（和ESP32 mbedtls_aes_crypt_ctr逻辑对齐）
                byte[] counter = new byte[16];
                Array.Copy(nonce, counter, 16);

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] keystream = new byte[input.Length];
                    int offset = 0;

                    // 生成和输入长度一致的密钥流
                    while (offset < keystream.Length)
                    {
                        byte[] block = encryptor.TransformFinalBlock(counter, 0, counter.Length);
                        int copyLen = Math.Min(block.Length, keystream.Length - offset);
                        Array.Copy(block, 0, keystream, offset, copyLen);
                        offset += copyLen;

                        // 递增counter（大端模式）
                        IncrementCounter(counter);
                    }

                    // 异或得到明文（CTR模式：明文 = 密文 XOR 密钥流）
                    byte[] output = new byte[input.Length];
                    for (int i = 0; i < input.Length; i++)
                    {
                        output[i] = (byte)(input[i] ^ keystream[i]);
                    }
                    return output;
                }
            }
        }
        /// <summary>
        /// AES-CTR模式解密（匹配mbedtls_aes_crypt_ctr）
        /// 入参改为32位十六进制字符串的key/nonce
        /// </summary>
        /// <param name="encryptedData">加密数据（字节数组）</param>
        /// <param name="hexKey">32位十六进制字符串的AES-128密钥（对应16字节）</param>
        /// <param name="hexNonce">32位十六进制字符串的Nonce（对应16字节）</param>
        /// <returns>解密后的原始字节数组</returns>
        /// <exception cref="ArgumentException">key/nonce格式错误时抛出</exception>
        public static byte[] AesCtrDecrypt(byte[] encryptedData, string hexKey, string hexNonce)
        {
            // 1. 空值/空数据校验
            if (encryptedData == null || encryptedData.Length == 0)
                return Array.Empty<byte>();

            // 2. 十六进制字符串转字节数组（核心修改）
            byte[] key = HexStringToBytes(hexKey);
            byte[] nonce = HexStringToBytes(hexNonce);

            // 3. 长度校验（确保转换后是16字节）
            if (key == null || key.Length != 16)
                throw new ArgumentException("AES密钥必须是32位十六进制字符串（对应16字节AES-128）", nameof(hexKey));
            if (nonce == null || nonce.Length != 16)
                throw new ArgumentException("Nonce必须是32位十六进制字符串（对应16字节）", nameof(hexNonce));

            // 4. 原有CTR解密逻辑（不变）
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB; // CTR模式手动实现
                aes.Padding = PaddingMode.None;
                aes.KeySize = 128;
                aes.Key = key;
                aes.IV = new byte[16]; // ECB模式无需IV

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] counter = (byte[])nonce.Clone();
                    byte[] decrypted = new byte[encryptedData.Length];
                    int blockSize = aes.BlockSize / 8; // 16字节
                    int blocks = (int)Math.Ceiling((double)encryptedData.Length / blockSize);

                    for (int i = 0; i < blocks; i++)
                    {
                        // 加密计数器得到流块
                        byte[] streamBlock = new byte[blockSize];
                        encryptor.TransformBlock(counter, 0, blockSize, streamBlock, 0);

                        // 与密文异或（CTR模式加解密逻辑相同）
                        int bytesToProcess = Math.Min(blockSize, encryptedData.Length - i * blockSize);
                        for (int j = 0; j < bytesToProcess; j++)
                        {
                            decrypted[i * blockSize + j] = (byte)(encryptedData[i * blockSize + j] ^ streamBlock[j]);
                        }

                        // 递增计数器（大端序）
                        IncrementCounter(counter);
                    }

                    return decrypted;
                }
            }
        }

        /// <summary>
        /// AES-CTR模式加密（匹配mbedtls_aes_crypt_ctr）
        /// 入参为32位十六进制字符串的key/nonce
        /// </summary>
        /// <param name="plainData">原始明文数据</param>
        /// <param name="hexKey">32位十六进制字符串的AES-128密钥</param>
        /// <param name="hexNonce">32位十六进制字符串的Nonce</param>
        /// <returns>加密后的字节数组</returns>
        /// <exception cref="ArgumentException">key/nonce格式错误时抛出</exception>
        public static byte[] AesCtrEncrypt(byte[] plainData, string hexKey, string hexNonce)
        {
            // CTR模式：加密和解密是完全相同的操作，直接复用解密逻辑
            return AesCtrDecrypt(plainData, hexKey, hexNonce);
        }

        #region 辅助函数
        /// <summary>
        /// 32位十六进制字符串转16字节数组（匹配GenerateFixedSizeHexString的输出）
        /// </summary>
        /// <param name="hexString">32位小写/大写十六进制字符串</param>
        /// <returns>16字节数组</returns>
        /// <exception cref="ArgumentException">格式错误时抛出</exception>
        private static byte[] HexStringToBytes(string hexString)
        {
            if (string.IsNullOrEmpty(hexString))
                throw new ArgumentNullException(nameof(hexString), "十六进制字符串不能为空");

            // 强制转为小写，兼容大小写输入
            hexString = hexString.Trim().ToLowerInvariant();

            // 校验长度：32位十六进制 = 16字节
            if (hexString.Length != 32)
                throw new ArgumentException("必须是32位十六进制字符串（对应16字节）", nameof(hexString));

            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                // 每2个字符对应1个字节
                int pos = i * 2;
                bytes[i] = Convert.ToByte(hexString.Substring(pos, 2), 16);
            }
            return bytes;
        }

        public static string ConvertUnicodeEscapeToUtf8String(this string unicodeEscapedString)
        {
            if (string.IsNullOrEmpty(unicodeEscapedString))
            {
                return string.Empty;
            }
            // 核心：使用正则匹配所有 \uXXXX 格式的序列并转换
            string decodedString = Regex.Replace(unicodeEscapedString,
                                                @"\\u([0-9A-Fa-f]{4})",
                                                match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
            // 此时 decodedString 已经是正常的C#字符串，例如：“嗨～是哪个小可爱找我啊？”
            return decodedString;
        }
        /// <summary>
        /// 递增计数器（大端序，匹配mbedtls的计数器规则）
        /// </summary>
        /// <param name="counter">16字节计数器</param>
        private static void IncrementCounter(byte[] counter)
        {
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0)
                    break; // 无进位，结束
            }
        }

        #endregion
    }
}
