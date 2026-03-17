using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class AudioPacketHelper
    {
        /// <summary>
        /// 滑动取样器：根据当前分析索引获取指定大小的帧数据。
        /// 每次调用会从 analyzedIndex 位置开始取 frameSize 个样本，并更新 analyzedIndex。
        /// </summary>
        /// <param name="audioData">音频数据数组</param>
        /// <param name="frameSize">每帧的样本数</param>
        /// <param name="analyzedIndex">当前已分析的索引位置（会被更新）</param>
        /// <param name="data">输出的帧数据</param>
        /// <returns>如果成功获取到足够的数据返回 true，否则返回 false</returns>
        public static bool GetSlidingFrame(this float[] audioData, int frameSize, ref int analyzedIndex, out float[] data)
        {
            try
            {
                if (audioData == null || audioData.Length == 0)
                {
                    data = Array.Empty<float>();
                    return false;
                }

                // 检查是否有足够的新数据可供分析
                int availableSamples = audioData.Length - analyzedIndex;
                if (availableSamples < frameSize)
                {
                    data = Array.Empty<float>();
                    return false;
                }

                // 从 analyzedIndex 位置开始提取 frameSize 个样本
                data = new float[frameSize];
                Array.Copy(audioData, analyzedIndex, data, 0, frameSize);

                // 更新已分析的索引位置
                analyzedIndex += frameSize;

                return true;
            }
            catch
            {
                data = Array.Empty<float>();
                return false;
            }
        }

        /// <summary>
        /// 将16位PCM字节数组转换为浮点数组  将16-bit little-endian PCM字节转换为归一化float
        /// </summary>
        /// <param name="pcmBytes">16位PCM字节数组</param>
        /// <returns>转换后的浮点数组，范围在[-1.0, 1.0]之间</returns>
        public static float[] Pcm16BytesToFloat(this byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length == 0)
                return Array.Empty<float>();

            int sampleCount = pcmBytes.Length / 2;
            float[] floats = new float[sampleCount];
            ReadOnlySpan<byte> span = pcmBytes;
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i * 2, 2));
                floats[i] = s / 32768f;
            }
            return floats;
        }

        /// <summary>
        /// 将指定比特深度的PCM字节数组转换为浮点数组
        /// </summary>
        /// <param name="pcmBytes">PCM字节数组</param>
        /// <param name="bitDepth">音频数据的比特深度（支持16、24、32位）</param>
        /// <returns>转换后的浮点数组，范围在[-1.0, 1.0]之间</returns>
        /// <exception cref="NotSupportedException">当比特深度不被支持时抛出异常</exception>
        public static float[] PcmBytesToFloat(this byte[] pcmBytes, int bitDepth)
        {
            if (pcmBytes == null || pcmBytes.Length == 0)
                return Array.Empty<float>();

            return bitDepth switch
            {
                16 => pcmBytes.Pcm16BytesToFloat(),
                24 => Convert24BitPcm(pcmBytes),
                32 => Convert32BitPcm(pcmBytes),
                _ => throw new NotSupportedException(string.Format(Lang.AudioPacketHelper_PcmBytesToFloat_UnsupportedBitDepth, bitDepth))
            };

            // 将24位PCM数据转换为浮点数组
            static float[] Convert24BitPcm(byte[] bytes)
            {
                int sampleCount = bytes.Length / 3;
                float[] floats = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    int index = i * 3;
                    int value = bytes[index] | (bytes[index + 1] << 8) | (bytes[index + 2] << 16);
                    // 24位有符号：如果最高位(第23位)为1，需要符号扩展
                    if ((value & 0x800000) != 0)
                        value |= unchecked((int)0xFF000000);
                    floats[i] = value / 8388608f; // 2^23
                }
                return floats;
            }

            // 将32位PCM数据转换为浮点数组
            static float[] Convert32BitPcm(byte[] bytes)
            {
                int sampleCount = bytes.Length / 4;
                float[] floats = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    int raw = BitConverter.ToInt32(bytes, i * 4);
                    floats[i] = raw / 2147483648f; // 2^31
                }
                return floats;
            }
        }

        /// <summary>
        /// 将浮点音频数据转换为指定比特深度的PCM字节数组
        /// </summary>
        /// <param name="audioData">浮点音频数据数组</param>
        /// <param name="bitDepth">目标比特深度（默认16位，支持16、24、32位）</param>
        /// <param name="channels">音频通道数（默认单声道）</param>
        /// <returns>转换后的PCM字节数组</returns>
        /// <exception cref="ArgumentException">当音频数据为空或比特深度不被支持时抛出异常</exception>
        public static byte[] Float2PcmBytes(this float[] audioData, int bitDepth = 16, int channels = 1)
        {
            if (audioData == null || audioData.Length == 0)
                throw new ArgumentException(nameof(audioData));
            if (bitDepth is not (16 or 24 or 32))
                throw new ArgumentException(Lang.AudioPacketHelper_Float2PcmBytes_UnsupportedFormat);

            int sampleCount = audioData.Length / channels;
            int bytesPerSample = bitDepth / 8;
            int totalBytes = sampleCount * channels * bytesPerSample;
            var pcmData = new List<byte>(totalBytes);

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = Math.Clamp(audioData[i], -1f, 1f);
                WriteSample(pcmData, sample, bitDepth);
            }
            return pcmData.ToArray();
        }

        /// <summary>
        /// 将单个浮点样本写入到PCM字节数组中
        /// </summary>
        /// <param name="pcmData">目标PCM字节数组列表</param>
        /// <param name="sample">浮点样本值（范围应在[-1.0, 1.0]之间）</param>
        /// <param name="bitDepth">目标比特深度（支持16、24、32位）</param>
        /// <exception cref="ArgumentException">当比特深度不被支持时抛出异常</exception>
        private static void WriteSample(List<byte> pcmData, float sample, int bitDepth)
        {
            switch (bitDepth)
            {
                case 16:
                    short pcm16 = (short)(sample * 32767f);
                    pcmData.AddRange(BitConverter.GetBytes(pcm16));
                    break;
                case 24:
                    int pcm24 = (int)(sample * 8388607f);
                    pcmData.Add((byte)(pcm24 & 0xFF));
                    pcmData.Add((byte)((pcm24 >> 8) & 0xFF));
                    pcmData.Add((byte)((pcm24 >> 16) & 0xFF));
                    break;
                case 32:
                    int pcm32 = (int)(sample * 2147483647f);
                    pcmData.AddRange(BitConverter.GetBytes(pcm32));
                    break;
                default:
                    throw new ArgumentException(string.Format(Lang.AudioPacketHelper_WriteSample_UnsupportedBitDepth, bitDepth));
            }
        }
    }
}
