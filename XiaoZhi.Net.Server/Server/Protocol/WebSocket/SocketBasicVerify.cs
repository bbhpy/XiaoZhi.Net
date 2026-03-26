using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;

namespace XiaoZhi.Net.Server.Server.Protocol.WebSocket
{
    internal class SocketBasicVerify : IBasicVerify
    {
        public bool Verify(string deviceId, string token, IPEndPoint userEndPoint)
        {
            // 测试模式：全部通过
             // 生产环境需要：
             // 1. 验证 deviceId 是否有效
             // 2. 验证 token 是否正确
             // 3. 可选：检查 IP 白名单等

            // 这里只是简单记录日志
            System.Diagnostics.Debug.WriteLine($"[认证] 设备: {deviceId}, Token: {token}, IP: {userEndPoint}");

            // 测试模式全部返回 true
            return true;
        }
    }
}
