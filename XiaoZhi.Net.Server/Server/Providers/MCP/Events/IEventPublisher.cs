using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.Events
{
    /// <summary>
    /// 事件发布器接口
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>
        /// 发布事件
        /// </summary>
        /// <typeparam name="T">事件类型</typeparam>
        /// <param name="event">事件对象</param>
        void Publish<T>(T @event) where T : notnull;

        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <typeparam name="T">事件类型</typeparam>
        /// <param name="handler">事件处理器</param>
        /// <returns>取消订阅的委托</returns>
        IDisposable Subscribe<T>(Action<T> handler) where T : notnull;
    }
}
