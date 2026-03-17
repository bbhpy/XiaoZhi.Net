using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP 会话管理器
    /// 对标 WebSocket 的 SessionContainer / ISessionContainer
    /// 因为 UDP 无连接，所以自己维护 Session 字典
    /// </summary>
    internal class UdpSessionManager
    {
        // 移除原有的本地字典，改为依赖统一的会话存储
        private readonly MqttUdpSessionStore _sessionStore;
        private readonly ProviderManager _providerManager;
        private readonly ILogger<UdpSessionManager> _logger;

        // 构造函数修改：注入统一的MqttUdpSessionStore
        public UdpSessionManager(
            MqttUdpSessionStore sessionStore,
            ProviderManager providerManager,
            ILogger<UdpSessionManager> logger)
        {
            _sessionStore = sessionStore;
            _providerManager = providerManager;
            _logger = logger;
        }

        /// <summary>
        /// 获取或创建 UDP 会话（兼容原有逻辑，基于IPEndPoint，同时关联SSRC）
        /// 对标 WebSocket 自动创建 Session
        /// </summary>
        public MqttUdpSession UpdateSessionUdp(uint ssrc,UdpClient udpClient, IPEndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            // 1. 先尝试通过IPEndPoint查找已有会话（兼容原有逻辑）
            var existingSession = _sessionStore.GetSessionBySsrc(ssrc);

            if (existingSession != null)
            {
                // 更新最后活跃时间
                existingSession.LastActiveTime = DateTime.Now;
                existingSession.UdpRemoteEndPoint = remoteEndPoint; // 更新IP地址（如果发生变化）
                _sessionStore.UpdateSession(existingSession);
                _logger.LogDebug("找到已有UDP会话：IP={IP}, SessionId={SessionId}",
                    remoteEndPoint, existingSession.SessionId);
                return existingSession;
            }

            // 4. 异常处理：添加失败时重试一次（防止并发冲突）
            _logger.LogWarning("ssrc不存在请检查程序逻辑，udp连接信息：IP={IP}", remoteEndPoint);

            return existingSession ?? throw new InvalidOperationException($"创建UDP会话失败：{remoteEndPoint}");
        }

        /// <summary>
        /// 移除 UDP 会话（超时/断开）
        /// 兼容原有方法签名，调整内部实现为操作统一存储
        /// </summary>
        public bool RemoveSession(IPEndPoint remoteEndPoint, out MqttUdpSession? session)
        {
            session = null;
            if (remoteEndPoint == null)
                return false;

            // 1. 查找对应会话
            session = _sessionStore.Get<MqttUdpSession>(s =>
                s.UdpRemoteEndPoint != null &&
                s.UdpRemoteEndPoint.Equals(remoteEndPoint)).FirstOrDefault();

            if (session == null)
            {
                _logger.LogDebug("移除UDP会话失败：未找到IP={IP}的会话", remoteEndPoint);
                return false;
            }

            // 2. 从统一存储移除
            var removeCount = _sessionStore.Remove(session.SessionId);
            if (removeCount > 0)
            {
                _logger.LogInformation("移除UDP会话：IP={IP}, SessionId={SessionId}",
                    remoteEndPoint, session.SessionId);
                // 释放资源（如有需要：比如关闭关联的MQTT连接、清理加密上下文等）
                return true;
            }

            _logger.LogWarning("移除UDP会话失败：SessionId={SessionId}移除计数为0", session.SessionId);
            return false;
        }

        /// <summary>
        /// 获取所有会话（用于超时扫描）
        /// 兼容原有方法，从统一存储读取
        /// </summary>
        public IEnumerable<MqttUdpSession> GetAllSessions()
        {
            return _sessionStore.GetAll<MqttUdpSession>().Values;
        }

        #region 私有辅助方法
        /// <summary>
        /// 生成唯一SessionId（IP+时间戳+随机数）
        /// </summary>
        private string GenerateUniqueSessionId(IPEndPoint remoteEndPoint)
        {
            var ipStr = remoteEndPoint.Address.ToString().Replace(".", "_");
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var random = new Random().Next(1000, 9999);
            return $"udp_{ipStr}_{remoteEndPoint.Port}_{timestamp}_{random}";
        }

        /// <summary>
        /// 生成随机SSRC（32位无符号整数，避免重复）
        /// </summary>
        private uint GenerateRandomSsrc()
        {
            var random = new Random();
            byte[] ssrcBytes = new byte[4];
            random.NextBytes(ssrcBytes);
            uint ssrc = BitConverter.ToUInt32(ssrcBytes, 0);

            // 确保SSRC不重复（简单校验，高并发可优化）
            while (_sessionStore.GetSessionBySsrc(ssrc) != null)
            {
                random.NextBytes(ssrcBytes);
                ssrc = BitConverter.ToUInt32(ssrcBytes, 0);
            }
            return ssrc;
        }
        #endregion
    }
}
