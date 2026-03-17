using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions.Middleware;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Middlewares
{
    /// <summary>
    /// 会话容器中间件，负责管理应用程序会话的注册、注销和查询操作
    /// </summary>
    internal class SessionContainerMiddleware : MiddlewareBase, ISessionContainer
    {
        /// <summary>
        /// 存储连接信息的数据存储对象
        /// </summary>
        private readonly IStore _connectionStore;

        /// <summary>
        /// 初始化 SessionContainerMiddleware 类的新实例
        /// </summary>
        /// <param name="store">用于存储会话数据的数据存储对象</param>
        public SessionContainerMiddleware(IStore store)
        {
            this._connectionStore = store;
            // 获取或设置中间件在管道中的执行顺序 为1000，确保它在管道中的适当位置执行
            this.Order = 1000;
        }

        /// <summary>
        /// 注册应用程序会话到容器中
        /// </summary>
        /// <param name="appSession">要注册的应用程序会话</param>
        /// <returns>表示异步操作的任务，返回是否成功注册</returns>
        public override ValueTask<bool> RegisterSession(IAppSession appSession)
        {
            // 检查会话是否需要握手，如果需要且未完成握手则直接返回
            if (appSession is IHandshakeRequiredSession handshakeSession)
            {
                if (!handshakeSession.Handshaked)
                    return ValueTask.FromResult(true);
            }

            // 处理Socket会话的注册
            if (appSession is SocketSession socketSession)
            {
                bool addResult = this._connectionStore.Add(appSession.SessionID, socketSession);

                if (!addResult)
                {
                    socketSession.Logger.LogWarning(Lang.SessionContainerMiddleware_RegisterSession_LoginFailed, socketSession.SessionID);
                    socketSession.CloseAsync(CloseReason.UnexpectedCondition, "Loggin failed");
                }
            }
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 从容器中注销应用程序会话
        /// </summary>
        /// <param name="session">要注销的应用程序会话</param>
        /// <returns>表示异步操作的任务，返回是否成功注销</returns>
        public override ValueTask<bool> UnRegisterSession(IAppSession session)
        {
            this._connectionStore.Remove(session.SessionID);
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 根据会话ID获取对应的会话对象
        /// </summary>
        /// <param name="sessionId">会话标识符</param>
        /// <returns>与指定ID关联的会话对象，如果不存在则返回null</returns>
        public IAppSession GetSessionByID(string sessionId)
        {
            return this._connectionStore.Get<IAppSession>(sessionId);
        }

        /// <summary>
        /// 获取当前存储的会话总数
        /// </summary>
        /// <returns>当前会话的数量</returns>
        public int GetSessionCount()
        {
            return this._connectionStore.GetAllCount();
        }

        /// <summary>
        /// 根据指定条件获取符合条件的会话集合
        /// </summary>
        /// <param name="criteria">用于筛选会话的谓词条件</param>
        /// <returns>满足条件的会话集合</returns>
        public IEnumerable<IAppSession> GetSessions(Predicate<IAppSession> criteria)
        {
            return this._connectionStore.Get(criteria);
        }

        /// <summary>
        /// 根据指定条件获取特定类型的会话集合
        /// </summary>
        /// <typeparam name="TAppSession">会话的具体类型，必须实现IAppSession接口</typeparam>
        /// <param name="criteria">用于筛选会话的谓词条件</param>
        /// <returns>满足条件的特定类型会话集合</returns>
        public IEnumerable<TAppSession> GetSessions<TAppSession>(Predicate<TAppSession> criteria) where TAppSession : IAppSession
        {
            return this._connectionStore.Get(criteria);
        }
    }
}
