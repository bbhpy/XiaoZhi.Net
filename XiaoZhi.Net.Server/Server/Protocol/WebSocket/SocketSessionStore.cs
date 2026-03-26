using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Server.Protocol.WebSocket
{
    /// <summary>
    /// WebSocket 会话存储类，继承 DefaultMemoryStore
    /// 专门用于存储和管理 WebSocket 的 Session 对象
    /// </summary>
    internal class SocketSessionStore : DefaultMemoryStore
    {
        /// <summary>
        /// 添加 WebSocket 会话，并自动维护索引
        /// </summary>
        public bool AddSession(string sessionId, SocketSession session)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentNullException(nameof(sessionId));
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            // 1. 调用父类的 Add 方法添加会话到主存储
            bool added = base.Add(sessionId, session);
            if (!added)
                return false;

            return true;
        }

        /// <summary>
        /// 获取 Session
        /// </summary>
        public SocketSession? GetSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            try
            {
                return base.Get<SocketSession>(sessionId);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 移除 Session，并清理索引
        /// </summary>
        public bool RemoveSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            // 1. 先获取会话（用于清理索引）
            SocketSession? session = null;
            try
            {
                session = base.Get<SocketSession>(sessionId);
            }
            catch
            {
                // 会话不存在，直接返回
                return false;
            }

            // 2. 调用父类 Remove 方法删除主数据
            int removed = base.Remove(sessionId);
            if (removed == 0)
                return false;

            return true;
        }

        /// <summary>
        /// 更新 Session
        /// </summary>
        public bool UpdateSession(string sessionId, Session session)
        {
            if (string.IsNullOrEmpty(sessionId) || session == null)
                return false;

            try
            {
                return base.Update(sessionId, session);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查 Session 是否存在
        /// </summary>
        public bool HasSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            return Contains(sessionId);
        }

        /// <summary>
        /// 清空所有会话和索引
        /// </summary>
        public override void Clear()
        {
            // 先清空父类的主数据
            base.Clear();
        }
    }
}