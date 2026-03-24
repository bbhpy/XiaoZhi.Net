using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP 工作项，用于在接收线程和 Worker 池之间传递数据
    /// 设计为轻量级数据结构，避免在接收线程中进行重操作
    /// </summary>
    internal class UdpWorkItem
    {
        /// <summary>
        /// 原始 UDP 数据包（未解密，未解析）
        /// </summary>
        public byte[] RawData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 从数据包中提取的 SSRC（用于路由到指定的 Worker）
        /// </summary>
        public uint Ssrc { get; set; }

        /// <summary>
        /// 客户端的远程端点（IP + 端口）
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; set; } = null!;

        /// <summary>
        /// 接收时间戳（用于超时诊断）
        /// </summary>
        public DateTime ReceivedTime { get; set; }

        /// <summary>
        /// 重置工作项，用于对象池复用（可选，当前未使用对象池）
        /// </summary>
        public void Reset()
        {
            RawData = Array.Empty<byte>();
            Ssrc = 0;
            RemoteEndPoint = null!;
            ReceivedTime = DateTime.MinValue;
        }
    }
}
