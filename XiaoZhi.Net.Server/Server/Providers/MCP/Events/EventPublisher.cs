using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.Events
{
    /// <summary>
    /// 内存事件总线实现
    /// </summary>
    public class EventPublisher : IEventPublisher, IDisposable
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _subscriptions = new();
        private readonly ReaderWriterLockSlim _lock = new();

        public void Publish<T>(T @event) where T : notnull
        {
            var eventType = typeof(T);
            List<Delegate> handlers;

            _lock.EnterReadLock();
            try
            {
                if (!_subscriptions.TryGetValue(eventType, out handlers))
                {
                    return;
                }
                // 复制一份，避免在遍历时被修改
                handlers = new List<Delegate>(handlers);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<T>)handler)(@event);
                }
                catch (Exception ex)
                {
                    // 日志记录由调用方负责，这里不引入日志依赖
                    System.Diagnostics.Debug.WriteLine($"事件处理异常: {ex.Message}");
                }
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : notnull
        {
            var eventType = typeof(T);

            _lock.EnterWriteLock();
            try
            {
                if (!_subscriptions.TryGetValue(eventType, out var handlers))
                {
                    handlers = new List<Delegate>();
                    _subscriptions[eventType] = handlers;
                }
                handlers.Add(handler);
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return new Subscription(this, eventType, handler);
        }

        private void Unsubscribe<T>(Action<T> handler) where T : notnull
        {
            var eventType = typeof(T);

            _lock.EnterWriteLock();
            try
            {
                if (_subscriptions.TryGetValue(eventType, out var handlers))
                {
                    handlers.Remove(handler);
                    if (handlers.Count == 0)
                    {
                        _subscriptions.TryRemove(eventType, out _);
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly EventPublisher _publisher;
            private readonly Type _eventType;
            private readonly Delegate _handler;

            public Subscription(EventPublisher publisher, Type eventType, Delegate handler)
            {
                _publisher = publisher;
                _eventType = eventType;
                _handler = handler;
            }

            public void Dispose()
            {
                var method = _publisher.GetType().GetMethod(nameof(Unsubscribe), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var genericMethod = method?.MakeGenericMethod(_eventType);
                genericMethod?.Invoke(_publisher, new[] { _handler });
            }
        }

        public void Dispose()
        {
            _lock.Dispose();
            _subscriptions.Clear();
        }
    }
}