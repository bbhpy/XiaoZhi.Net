using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Common.Constants;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{

    /// <summary>
    /// 工具路由器 - 只负责工具名到目标的映射查询
    /// 不管理WebSocket连接，不处理调用
    /// </summary>
    internal class ToolRouter
    {
        private readonly ILogger<ToolRouter> _logger;

        // 工具名 -> 该工具所属的所有(设备Token, 服务ID)列表
        private readonly ConcurrentDictionary<string, List<(string DeviceToken, string ServiceId)>> _toolMap = new();

        public ToolRouter(ILogger<ToolRouter> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 注册工具路由
        /// </summary>
        public void RegisterRoute(string toolName, string deviceToken, string serviceId)
        {
            if (!StaticDeputy.IsValidToolName(toolName))
            {
                _logger.LogWarning("工具名称 {ToolName} 包含非法字符，已跳过", toolName);
                return;
            }

            var list = _toolMap.GetOrAdd(toolName, _ => new List<(string, string)>());

            lock (list)
            {
                if (!list.Any(x => x.DeviceToken == deviceToken && x.ServiceId == serviceId))
                {
                    list.Add((deviceToken, serviceId));
                    _logger.LogDebug("工具路由注册: {ToolName} -> 设备 {DeviceToken}, 服务 {ServiceId}",
                        toolName, deviceToken, serviceId);
                }
            }
        }

        /// <summary>
        /// 批量注册工具路由
        /// </summary>
        public void RegisterRoutes(IEnumerable<string> toolNames, string deviceToken, string serviceId)
        {
            foreach (var toolName in toolNames)
            {
                RegisterRoute(toolName, deviceToken, serviceId);
            }
        }

        /// <summary>
        /// 移除设备的所有路由
        /// </summary>
        public void UnregisterDeviceRoutes(string deviceToken)
        {
            var removedCount = 0;
            foreach (var toolName in _toolMap.Keys.ToList())
            {
                if (_toolMap.TryGetValue(toolName, out var list))
                {
                    lock (list)
                    {
                        var beforeCount = list.Count;
                        list.RemoveAll(x => x.DeviceToken == deviceToken);
                        removedCount += beforeCount - list.Count;
                    }
                }
            }

            if (removedCount > 0)
            {
                _logger.LogDebug("移除设备 {DeviceToken} 的 {Count} 个路由", deviceToken, removedCount);
            }
        }

        /// <summary>
        /// 移除指定服务和设备的路由
        /// </summary>
        public void UnregisterServiceRoutes(string deviceToken, string serviceId)
        {
            foreach (var toolName in _toolMap.Keys.ToList())
            {
                if (_toolMap.TryGetValue(toolName, out var list))
                {
                    lock (list)
                    {
                        list.RemoveAll(x => x.DeviceToken == deviceToken && x.ServiceId == serviceId);
                    }
                }
            }

            _logger.LogDebug("移除设备 {DeviceToken} 服务 {ServiceId} 的路由", deviceToken, serviceId);
        }

        /// <summary>
        /// 根据工具名查找所有可用的目标
        /// </summary>
        public List<(string DeviceToken, string ServiceId)> FindTargets(string toolName)
        {
            if (_toolMap.TryGetValue(toolName, out var list))
            {
                lock (list)
                {
                    return list.ToList();
                }
            }
            return new List<(string, string)>();
        }

        /// <summary>
        /// 检查工具是否存在
        /// </summary>
        public bool HasTool(string toolName)
        {
            return _toolMap.ContainsKey(toolName);
        }

        /// <summary>
        /// 获取所有已注册的工具名
        /// </summary>
        public List<string> GetAllToolNames()
        {
            return _toolMap.Keys.ToList();
        }

        /// <summary>
        /// 获取设备的所有工具名
        /// </summary>
        public List<string> GetDeviceToolNames(string deviceToken)
        {
            var result = new List<string>();
            foreach (var kvp in _toolMap)
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.Any(x => x.DeviceToken == deviceToken))
                    {
                        result.Add(kvp.Key);
                    }
                }
            }
            return result;
        }
    }
}