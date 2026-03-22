using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;
using XiaoZhi.Net.Server.Server.Providers.MCP.Events;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// Token 会话注册表
    /// 维护 Token 和 Session 的映射关系
    /// </summary>
    internal class TokenSessionRegistry
    {
        private readonly ConcurrentDictionary<string, TokenSessionInfo> _tokenSessions = new();
        private readonly ILogger<TokenSessionRegistry> _logger;

        private readonly IEventPublisher? _eventPublisher;
        // ⭐ 注入 Session 容器
        private readonly ISessionContainer _sessionContainer;
        private readonly MqttUdpSessionStore _mqttSessionStore;

        public TokenSessionRegistry(
     ILogger<TokenSessionRegistry> logger,
     ISessionContainer sessionContainer,
     MqttUdpSessionStore mqttSessionStore,
     IEventPublisher? eventPublisher = null)  // 可选参数，保持向后兼容
        {
            _logger = logger;
            _sessionContainer = sessionContainer;
            _mqttSessionStore = mqttSessionStore;
            _eventPublisher = eventPublisher;
        }

        /// <summary>
        /// 根据 token 获取 Session 对象
        /// </summary>
        public Session? GetSession(string token)
        {
            if (!_tokenSessions.TryGetValue(token, out var info))
            {
                _logger.LogDebug("Token {Token} 未找到对应的 Session 信息", token);
                return null;
            }

            // 从 MQTT/UDP Session 容器中查找
            var mqttSession = _mqttSessionStore.GetSession(info.SessionId);
            if (mqttSession != null)
            {
                _logger.LogDebug("Token {Token} 从 MQTT 容器找到 Session {SessionId}", token, info.SessionId);
                return mqttSession.XiaoZhiSession;
            }

            // 优先从 WebSocket Session 容器中查找\
            var wsSession = _sessionContainer.GetSessionByID(info.SessionId) as SocketSession;
            if (wsSession!=null)
            {
                _logger.LogDebug("Token {Token} 从 WebSocket 容器找到 Session {SessionId}", token, info.SessionId);
                return wsSession.XiaoZhiSession;
            }

            _logger.LogWarning("Token {Token} 对应的 Session {SessionId} 不存在", token, info.SessionId);
            return null;
        }

        /// <summary>
        /// 根据 token 获取设备ID
        /// </summary>
        public string? GetDeviceId(string token)
        {
            return _tokenSessions.TryGetValue(token, out var info) ? info.DeviceId : null;
        }

        /// <summary>
        /// 根据 token 获取 sessionId
        /// </summary>
        public string? GetSessionId(string token)
        {
            return _tokenSessions.TryGetValue(token, out var info) ? info.SessionId : null;
        }

        /// <summary>
        /// 注册 token 与 sessionId 的绑定
        /// </summary>
        public void Register(string token, string sessionId, string? deviceId = null)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sessionId))
                return;

            var info = new TokenSessionInfo
            {
                Token = token,
                SessionId = sessionId,
                DeviceId = deviceId,
                RegisteredAt = DateTime.UtcNow,
                LastActive = DateTime.UtcNow
            };

            _tokenSessions[token] = info;
            _logger.LogDebug("Token registered: {Token} -> Session {SessionId}, Device {DeviceId}",
                token, sessionId, deviceId ?? "unknown");

            // 发布设备上线事件
            _eventPublisher?.Publish(new DeviceOnlineEvent(token, sessionId, DateTime.UtcNow));
        }

        /// <summary>
        /// 获取完整的token信息
        /// </summary>
        public TokenSessionInfo? GetTokenInfo(string token)
        {
            return _tokenSessions.TryGetValue(token, out var info) ? info : null;
        }

        /// <summary>
        /// 验证 token 是否有效
        /// </summary>
        public bool ValidateToken(string token)
        {
            if (!_tokenSessions.TryGetValue(token, out var info))
                return false;

            // 验证 Session 是否仍然存在
            var session = GetSession(token);
            return session != null;
        }

        /// <summary>
        /// 更新token最后活动时间
        /// </summary>
        public void UpdateActivity(string token)
        {
            if (_tokenSessions.TryGetValue(token, out var info))
            {
                info.LastActive = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 移除绑定
        /// </summary>
        public void Unregister(string token)
        {
            if (_tokenSessions.TryRemove(token, out var info))
            {
                _logger.LogDebug("Token unregistered: {Token} -> Session {SessionId}, Device {DeviceId}",
                    token, info.SessionId, info.DeviceId ?? "unknown");

                // 发布设备离线事件
                _eventPublisher?.Publish(new DeviceOfflineEvent(token, DateTime.UtcNow));
            }
        }

        /// <summary>
        /// 获取所有在线 token 数量
        /// </summary>
        public int Count => _tokenSessions.Count;

        /// <summary>
        /// 获取所有在线token
        /// </summary>
        public List<string> GetAllTokens()
        {
            return _tokenSessions.Keys.ToList();
        }

        /// <summary>
        /// Token会话信息
        /// </summary>
        public class TokenSessionInfo
        {
            public string Token { get; set; } = string.Empty;
            public string SessionId { get; set; } = string.Empty;
            public string? DeviceId { get; set; }
            public DateTime RegisteredAt { get; set; }
            public DateTime LastActive { get; set; }
        }
    }
}