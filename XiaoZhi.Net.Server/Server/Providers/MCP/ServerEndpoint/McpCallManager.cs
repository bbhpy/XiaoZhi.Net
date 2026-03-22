using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// MCP调用管理器
    /// 负责管理调用等待和响应匹配
    /// </summary>
    internal class McpCallManager
    {
        private readonly ILogger<McpCallManager> _logger;

        // 调用ID -> 任务完成源
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pendingCalls = new();

        // 调用ID生成器
        private int _nextCallId = 1000;

        public McpCallManager(ILogger<McpCallManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 生成下一个调用ID
        /// </summary>
        public int NextCallId => Interlocked.Increment(ref _nextCallId);

        /// <summary>
        /// 注册等待的调用
        /// </summary>
        public Task<JsonObject> RegisterPendingCall(int callId)
        {
            var tcs = new TaskCompletionSource<JsonObject>();
            if (_pendingCalls.TryAdd(callId, tcs))
            {
                return tcs.Task;
            }
            throw new InvalidOperationException($"调用ID {callId} 已存在");
        }

        /// <summary>
        /// 完成等待的调用
        /// </summary>
        public bool CompletePendingCall(int callId, JsonObject result)
        {
            if (_pendingCalls.TryRemove(callId, out var tcs))
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult(result);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 拒绝等待的调用（设置异常）
        /// </summary>
        public bool RejectPendingCall(int callId, Exception exception)
        {
            if (_pendingCalls.TryRemove(callId, out var tcs))
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetException(exception);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 拒绝等待的调用（设置错误消息）
        /// </summary>
        public bool RejectPendingCall(int callId, string errorMessage)
        {
            return RejectPendingCall(callId, new Exception(errorMessage));
        }

        /// <summary>
        /// 清理指定调用（如果存在且未完成）
        /// </summary>
        public bool CleanPendingCall(int callId)
        {
            return _pendingCalls.TryRemove(callId, out _);
        }

        /// <summary>
        /// 清理所有等待的调用（设备离线时调用）
        /// </summary>
        public void CleanAllPendingCalls(string deviceToken, string serviceId)
        {
            // 注意：这里无法按设备清理，因为调用ID没有关联设备信息
            // 如果需要精确清理，可以在调用时存储更多上下文
            // 暂时只记录日志
            _logger.LogDebug("清理所有等待调用（设备 {DeviceToken} 服务 {ServiceId}）", deviceToken, serviceId);
        }

        /// <summary>
        /// 获取当前等待调用数量
        /// </summary>
        public int PendingCallCount => _pendingCalls.Count;
    }
}