using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    internal class UdpAudioPacket
    {
        /// <summary>
        /// 数据包类型（固定0x01）
        /// </summary>
        public byte Type { get; set; } = 0x01;

        /// <summary>
        /// 标志位（未使用）
        /// </summary>
        public byte Flags { get; set; } = 0x00;

        /// <summary>
        /// 负载长度（网络字节序）
        /// </summary>
        public ushort PayloadLength { get; set; }

        /// <summary>
        /// 同步源标识符
        /// </summary>
        public uint Ssrc { get; set; }

        /// <summary>
        /// 时间戳（网络字节序）
        /// </summary>
        public uint Timestamp { get; set; }

        /// <summary>
        /// 序列号（网络字节序）
        /// </summary>
        public uint Sequence { get; set; }

        /// <summary>
        /// 加密的Opus音频数据
        /// </summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 从字节数组解析数据包
        /// </summary>
        public static bool TryParse(byte[] data, out UdpAudioPacket packet)
        {
            packet = null;
            try
            {
                packet = Parse(data);
                // 最小长度校验：1+1+2+4+4+4 = 16字节
                //if (data == null || data.Length < 16)
                //    return false;

                //packet = new UdpAudioPacket
                //{
                //    Type = data[0],
                //    Flags = data[1],
                //    PayloadLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2)),
                //    Ssrc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4)),
                //    Timestamp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)),
                //    Sequence = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4))
                //};

                //// 校验类型和负载长度
                //if (packet.Type != 0x01)
                //    return false;
                //if (data.Length < 16 + packet.PayloadLength)
                //    return false;

                //// 读取负载
                //packet.Payload = new byte[packet.PayloadLength];
                //Array.Copy(data, 16, packet.Payload, 0, packet.PayloadLength);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析UDP音频包（严格按格式解析，处理网络字节序）
        /// </summary>
        /// <param name="buffer">UDP接收的原始字节数组</param>
        /// <returns>解析后的音频包对象</returns>
        /// <exception cref="ArgumentException">包格式错误时抛出</exception>
        public static UdpAudioPacket Parse(byte[] buffer)
        {
            // 1. 基础长度校验：头部至少16字节（1+1+2+4+4+4）
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer), "UDP数据包不能为空");

            const int headerLength = 16; // 固定头部长度
            if (buffer.Length < headerLength)
                throw new ArgumentException($"UDP数据包长度不足，最小需要{headerLength}字节，实际{buffer.Length}字节", nameof(buffer));

            var packet = new UdpAudioPacket();
            try
            {
                // 2. 逐字段解析（.NET 8 推荐用 BinaryPrimitives 处理字节序，比 IPAddress.NetworkToHostOrder 更通用）
                // Type: 1字节，偏移0
                packet.Type = buffer[0];
                if (packet.Type != 0x01)
                    throw new InvalidDataException($"非法的数据包类型：0x{packet.Type:X2}，预期0x01");

                // Flags: 1字节，偏移1
                packet.Flags = buffer[1];

                // PayloadLength: 2字节，偏移2，网络字节序（大端）→ 主机序
                packet.PayloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));

                // SSRC: 4字节，偏移4，网络字节序→主机序
                packet.Ssrc = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4, 4));

                // Timestamp: 4字节，偏移8，网络字节序→主机序
                packet.Timestamp = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8, 4));

                // Sequence: 4字节，偏移12，网络字节序→主机序
                packet.Sequence = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12, 4));

                // 3. 解析Payload（核心修正：计算剩余长度，替代 RemainingLength 方法）
                // 剩余长度 = 总长度 - 头部长度
                int remainingLength = buffer.Length - headerLength;
                if (remainingLength != packet.PayloadLength)
                {
                    throw new InvalidDataException(
                        $"Payload长度不匹配：头部声明{packet.PayloadLength}字节，实际剩余{remainingLength}字节");
                }

                // 读取Payload（偏移16，长度=PayloadLength）
                packet.Payload = buffer.AsSpan(headerLength, packet.PayloadLength).ToArray();
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("UDP数据包解析错误", ex);
            }
            return packet;
        }
    }

    /// <summary>
    /// 客户端序列号管理
    /// </summary>
    public class ClientSequenceManager
    {
        // SSRC → 最新期望值序列号
        private readonly Dictionary<uint, uint> _sequenceMap = new Dictionary<uint, uint>();
        private readonly object _lockObj = new object();

        /// <summary>
        /// 校验序列号（防重放+容错）
        /// </summary>
        public bool ValidateSequence(uint ssrc, uint sequence, out string errorMsg)
        {
            lock (_lockObj)
            {
                errorMsg = null;
                if (!_sequenceMap.TryGetValue(ssrc, out uint expectedSeq))
                {
                    // 首次连接，初始化期望值
                    _sequenceMap[ssrc] = sequence + 1;
                    return true;
                }

                // 防重放：拒绝小于期望值的数据包
                if (sequence < expectedSeq)
                {
                    errorMsg = $"序列号异常（重放）：SSRC={ssrc}，当前={sequence}，期望≥{expectedSeq}";
                    return false;
                }

                // 容错：轻微跳跃（≤5）记录警告，过大则标记异常
                if (sequence > expectedSeq)
                {
                    uint gap = sequence - expectedSeq;
                    if (gap > 5)
                    {
                        errorMsg = $"序列号跳跃过大：SSRC={ssrc}，当前={sequence}，期望={expectedSeq}，跳跃={gap}";
                    }
                    // 更新期望值为当前+1
                    _sequenceMap[ssrc] = sequence + 1;
                    return true;
                }

                // 序列号连续
                _sequenceMap[ssrc] = expectedSeq + 1;
                return true;
            }
        }

        /// <summary>
        /// 移除客户端序列号记录
        /// </summary>
        public void RemoveClient(uint ssrc)
        {
            lock (_lockObj)
            {
                _sequenceMap.Remove(ssrc);
            }
        }
    }
}
