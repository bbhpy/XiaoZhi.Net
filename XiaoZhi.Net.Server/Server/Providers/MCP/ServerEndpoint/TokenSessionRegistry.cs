using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// Token 会话注册表
    /// 维护 Token 和 SessionId 的映射关系
    /// </summary>
    public class TokenSessionRegistry
    {
        private readonly ConcurrentDictionary<string, TokenSessionInfo> _tokenSessions = new();
        private readonly ILogger<TokenSessionRegistry> _logger;

        public TokenSessionRegistry(ILogger<TokenSessionRegistry> logger)
        {
            _logger = logger;
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
        }

        /// <summary>
        /// 根据 token 获取 sessionId
        /// </summary>
        public string? GetSessionId(string token)
        {
            return _tokenSessions.TryGetValue(token, out var info) ? info.SessionId : null;
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
            return _tokenSessions.ContainsKey(token);
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
            public string Token { get; set; }
            public string SessionId { get; set; }
            public string? DeviceId { get; set; }
            public DateTime RegisteredAt { get; set; }
            public DateTime LastActive { get; set; }
        }
    }
}
