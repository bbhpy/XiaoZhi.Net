using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Server.Providers.MCP
{
    /// <summary>
    /// MCP服务存储类 - 纯数据存储
    /// 负责持久化设备与服务之间的绑定关系
    /// </summary>
    internal class McpServiceStore : DefaultMemoryStore
    {
        // 存储键格式: "binding:{deviceToken}:{serviceId}"
        private const string KEY_PREFIX = "binding:";

        /// <summary>
        /// 生成存储键
        /// </summary>
        private static string GetKey(string deviceToken, string serviceId)
        {
            return $"{KEY_PREFIX}{deviceToken}:{serviceId}";
        }

        /// <summary>
        /// 保存或更新服务绑定
        /// </summary>
        public bool SaveBinding(ServiceBinding binding)
        {
            if (binding == null)
                return false;

            var key = GetKey(binding.DeviceToken, binding.ServiceId);
            binding.LastUpdatedAt = DateTime.UtcNow;

            if (Contains(key))
            {
                return Update(key, binding);
            }
            else
            {
                return Add(key, binding);
            }
        }

        /// <summary>
        /// 获取指定设备和服务的绑定
        /// </summary>
        public ServiceBinding? GetBinding(string deviceToken, string serviceId)
        {
            var key = GetKey(deviceToken, serviceId);
            return SafeGetBinding(key);
        }

        /// <summary>
        /// 删除指定设备和服务的绑定
        /// </summary>
        public int DeleteBinding(string deviceToken, string serviceId)
        {
            var key = GetKey(deviceToken, serviceId);
            return Remove(key);
        }

        /// <summary>
        /// 获取设备的所有绑定
        /// </summary>
        public List<ServiceBinding> GetBindingsByDevice(string deviceToken)
        {
            var result = new List<ServiceBinding>();
            var allBindings = GetAll<ServiceBinding>();

            foreach (var binding in allBindings.Values)
            {
                if (binding.DeviceToken == deviceToken)
                {
                    result.Add(binding);
                }
            }
            return result;
        }

        /// <summary>
        /// 获取设备的所有工具定义
        /// </summary>
        public List<ToolDefinition> GetAllToolsByDevice(string deviceToken)
        {
            var bindings = GetBindingsByDevice(deviceToken);
            return bindings
                .Where(b => b.Tools != null)
                .SelectMany(b => b.Tools!)
                .ToList();
        }

        /// <summary>
        /// 获取设备指定服务的工具定义
        /// </summary>
        public List<ToolDefinition> GetToolsByService(string deviceToken, string serviceId)
        {
            var binding = GetBinding(deviceToken, serviceId);
            return binding?.Tools ?? new List<ToolDefinition>();
        }

        /// <summary>
        /// 更新工具列表
        /// </summary>
        public bool UpdateTools(string deviceToken, string serviceId, List<ToolDefinition> tools)
        {
            var binding = GetBinding(deviceToken, serviceId);
            if (binding == null)
                return false;

            binding.Tools = tools;
            binding.LastToolsUpdateAt = DateTime.UtcNow;
            binding.LastUpdatedAt = DateTime.UtcNow;

            return SaveBinding(binding);
        }

        /// <summary>
        /// 获取所有绑定了指定设备的服务（用于反向查询）
        /// </summary>
        public List<ServiceBinding> GetBindingsByService(string serviceId)
        {
            var result = new List<ServiceBinding>();
            var allBindings = GetAll<ServiceBinding>();

            foreach (var binding in allBindings.Values)
            {
                if (binding.ServiceId == serviceId)
                {
                    result.Add(binding);
                }
            }
            return result;
        }

        /// <summary>
        /// 获取所有在线服务的绑定（有活跃连接的）
        /// </summary>
        public List<ServiceBinding> GetOnlineServiceBindings()
        {
            var result = new List<ServiceBinding>();
            var allBindings = GetAll<ServiceBinding>();

            foreach (var binding in allBindings.Values)
            {
                if (!string.IsNullOrEmpty(binding.CurrentConnectionId))
                {
                    result.Add(binding);
                }
            }
            return result;
        }

        /// <summary>
        /// 安全获取绑定
        /// </summary>
        public ServiceBinding? SafeGetBinding(string deviceToken, string serviceId)
        {
            return GetBinding(deviceToken, serviceId);
        }

        /// <summary>
        /// 安全获取绑定（通过键）
        /// </summary>
        public ServiceBinding? SafeGetBinding(string key)
        {
            if (!Contains(key))
                return null;

            try
            {
                return Get<ServiceBinding>(key);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 批量加载绑定数据
        /// </summary>
        public void LoadBindings(List<ServiceBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                var key = GetKey(binding.DeviceToken, binding.ServiceId);
                Add(key, binding);
            }
        }
    }

    /// <summary>
    /// 服务绑定类
    /// </summary>
    internal class ServiceBinding
    {
        /// <summary>
        /// 设备标识
        /// </summary>
        public string DeviceToken { get; set; } = string.Empty;

        /// <summary>
        /// 三方服务ID（如 "home-assistant"）
        /// </summary>
        public string ServiceId { get; set; } = string.Empty;

        /// <summary>
        /// 显示名称（如 "Home Assistant"）
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// 工具列表（完整定义）
        /// </summary>
        public List<ToolDefinition>? Tools { get; set; }

        /// <summary>
        /// 首次连接时间
        /// </summary>
        public DateTime FirstConnectedAt { get; set; }

        /// <summary>
        /// 最后连接时间
        /// </summary>
        public DateTime LastConnectedAt { get; set; }

        /// <summary>
        /// 最后工具更新时间
        /// </summary>
        public DateTime LastToolsUpdateAt { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdatedAt { get; set; }

        /// <summary>
        /// 当前连接ID，为空表示离线
        /// </summary>
        public string CurrentConnectionId { get; set; } = string.Empty;

        /// <summary>
        /// 获取绑定的复合键
        /// </summary>
        public string GetBindingKey() => $"binding:{DeviceToken}:{ServiceId}";
    }
}
