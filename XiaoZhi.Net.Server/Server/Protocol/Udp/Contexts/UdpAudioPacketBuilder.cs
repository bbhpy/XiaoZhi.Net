using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP音频包打包工具（适配多终端高负载场景）
    /// </summary>
    internal static class UdpAudioPacketBuilder
    {
        // 多终端序列号/时间戳管理器（线程安全，支持高并发）
        private static readonly ConcurrentDictionary<uint, TerminalAudioState> _terminalStates = new();

        // 内存池：复用字节数组，减少高负载下的GC压力（.NET 8+推荐）
        private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;

        // OPUS帧固定时间增量（与ESP32端保持一致：60ms/帧）
        private const uint TimestampIncrement = 60;

        /// <summary>
        /// 终端音频状态（每个SSRC独立，线程安全）
        /// </summary>
        private class TerminalAudioState
        {
            // 原子操作保证高并发下序列号/时间戳准确性
            private uint _sequence;
            private uint _timestamp;

            /// <summary>
            /// 获取下一个序列号（原子自增）
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public uint GetNextSequence() => Interlocked.Increment(ref _sequence);

            /// <summary>
            /// 获取下一个时间戳（原子累加）
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public uint GetNextTimestamp() => Interlocked.Add(ref _timestamp, TimestampIncrement) - TimestampIncrement;

            /// <summary>
            /// 重置终端状态（断开连接时调用）
            /// </summary>
            public void Reset()
            {
                Interlocked.Exchange(ref _sequence, 0);
                Interlocked.Exchange(ref _timestamp, 0);
            }
        }

        /// <summary>
        /// 打包OPUS音频数据为ESP32可解析的UDP二进制格式
        /// </summary>
        /// <param name="opusData">未加密的原始OPUS音频数据</param>
        /// <param name="ssrc">终端唯一SSRC（从session中获取）</param>
        /// <returns>可直接发送给ESP32的二进制数据包</returns>
        /// <exception cref="ArgumentNullException">音频数据为空</exception>
        /// <exception cref="ArgumentException">音频数据长度超出范围</exception>
        public static byte[] BuildUdpAudioPacket(byte[] opusData, uint ssrc)
        {
            // 1. 入参校验（高负载下提前校验，避免后续异常）
            ArgumentNullException.ThrowIfNull(opusData);
            if (opusData.Length == 0)
                throw new ArgumentException("OPUS音频数据不能为空", nameof(opusData));
            if (opusData.Length > ushort.MaxValue)
                throw new ArgumentException($"OPUS数据长度不能超过{ushort.MaxValue}字节", nameof(opusData));

            // 2. 获取/初始化终端状态（线程安全，支持高并发）
            var terminalState = _terminalStates.GetOrAdd(ssrc, _ => new TerminalAudioState());

            // 3. 原子获取序列号和时间戳（避免高并发下重复/错乱）
            var sequence = terminalState.GetNextSequence();
            var timestamp = terminalState.GetNextTimestamp();

            // 4. 计算数据包总长度（头部16字节 + 负载长度）
            const int headerLength = 16;
            int totalLength = headerLength + opusData.Length;

            // 5. 从内存池获取缓冲区（高负载下减少GC）
            byte[] buffer = _bytePool.Rent(totalLength);
            try
            {
                // 6. 构建包头（严格按ESP32解析格式，网络字节序）
                var span = buffer.AsSpan(0, totalLength);

                // Type: 1字节，固定0x01
                span[0] = 0x01;

                // Flags: 1字节，固定0x00
                span[1] = 0x00;

                // PayloadLength: 2字节，网络字节序（大端）
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), (ushort)opusData.Length);

                // SSRC: 4字节，网络字节序（大端）
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), ssrc);

                // Timestamp: 4字节，网络字节序（大端）
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), timestamp);

                // Sequence: 4字节，网络字节序（大端）
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), sequence);

                // 7. 拷贝OPUS负载（未加密）
                opusData.AsSpan().CopyTo(span.Slice(headerLength));

                // 8. 复制到最终数组（内存池缓冲区可能大于实际长度）
                var result = new byte[totalLength];
                span.Slice(0, totalLength).CopyTo(result);

                return result;
            }
            finally
            {
                // 归还内存池缓冲区（高负载下关键，避免内存泄漏）
                _bytePool.Return(buffer);
            }
        }

        /// <summary>
        /// 重置指定终端的音频状态（断开连接时调用）
        /// </summary>
        /// <param name="ssrc">终端SSRC</param>
        public static void ResetTerminalState(uint ssrc)
        {
            if (_terminalStates.TryGetValue(ssrc, out var state))
            {
                state.Reset();
            }
        }

        /// <summary>
        /// 清理指定终端的状态（彻底断开时调用，释放内存）
        /// </summary>
        /// <param name="ssrc">终端SSRC</param>
        public static void RemoveTerminalState(uint ssrc)
        {
            _terminalStates.TryRemove(ssrc, out _);
        }

        /// <summary>
        /// 清理所有终端状态（服务端重启/维护时调用）
        /// </summary>
        public static void ClearAllTerminalStates()
        {
            _terminalStates.Clear();
        }
    }
}