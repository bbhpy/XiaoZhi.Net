using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Server.Providers.MCP
{
    /// <summary>
    /// MCP服务存储类，用于管理设备与服务之间的绑定关系
    /// </summary>
    internal class McpServiceStore : DefaultMemoryStore
    {
        /// <summary>
        /// 根据设备令牌获取该设备绑定的所有服务
        /// </summary>
        public List<ServiceBinding> GetServicesByDevice(string deviceToken)
        {
            var result = new List<ServiceBinding>();
            var allBindings = this.GetAll<ServiceBinding>();

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
        /// 根据服务ID获取使用该服务的所有设备
        /// </summary>
        public List<ServiceBinding> GetDevicesByService(string serviceId)
        {
            var result = new List<ServiceBinding>();
            var allBindings = this.GetAll<ServiceBinding>();

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
        /// 更新设备服务绑定的工具列表
        /// </summary>
        public bool UpdateTools(string deviceToken, string serviceId, List<ToolDefinition> tools)
        {
            var key = $"binding:{deviceToken}:{serviceId}";
            var binding = this.SafeGetBinding(key);

            if (binding != null)
            {
                binding.Tools = tools;
                binding.LastToolsUpdateAt = DateTime.UtcNow;
                return this.Update(key, binding);
            }
            return false;
        }

        /// <summary>
        /// 更新设备服务绑定的连接状态
        /// </summary>
        public bool UpdateConnectionStatus(string deviceToken, string serviceId,
            string connectionId, bool isConnected)
        {
            var key = $"binding:{deviceToken}:{serviceId}";
            var binding = this.SafeGetBinding(key);

            if (binding != null)
            {
                binding.CurrentConnectionId = isConnected ? connectionId : null;
                binding.LastConnectedAt = DateTime.UtcNow;
                return this.Update(key, binding);
            }
            return false;
        }

        /// <summary>
        /// 获取所有在线服务
        /// </summary>
        public List<ServiceBinding> GetOnlineServices()
        {
            var result = new List<ServiceBinding>();
            var allBindings = this.GetAll<ServiceBinding>();

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
        /// 从数据库批量加载服务绑定数据
        /// </summary>
        public void LoadFromDatabase(List<ServiceBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                var key = $"binding:{binding.DeviceToken}:{binding.ServiceId}";
                this.Add(key, binding);
            }
        }

        /// <summary>
        /// 安全获取绑定
        /// </summary>
        public ServiceBinding? SafeGetBinding(string deviceToken, string serviceId)
        {
            var key = $"binding:{deviceToken}:{serviceId}";
            return SafeGetBinding(key);
        }

        /// <summary>
        /// 安全获取绑定
        /// </summary>
        public ServiceBinding? SafeGetBinding(string key)
        {
            if (!this.Contains(key))
            {
                return null;
            }

            try
            {
                return this.Get<ServiceBinding>(key);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 根据设备Token获取所有工具名称（快速索引用）
        /// </summary>
        public List<string> GetAllToolNamesByDevice(string deviceToken)
        {
            var services = GetServicesByDevice(deviceToken);
            return services.SelectMany(s => s.Tools?.Select(t => t.Name) ?? new List<string>()).ToList();
        }

        /// <summary>
        /// 根据设备Token获取所有工具的完整定义
        /// </summary>
        public List<ToolDefinition> GetAllToolsByDevice(string deviceToken)
        {
            var services = GetServicesByDevice(deviceToken);
            return services.SelectMany(s => s.Tools ?? new List<ToolDefinition>()).ToList();
        }

        /// <summary>
        /// 检查工具是否属于指定设备
        /// </summary>
        public bool IsToolBelongToDevice(string deviceToken, string toolName)
        {
            var services = GetServicesByDevice(deviceToken);
            return services.Any(s => s.Tools?.Any(t => t.Name == toolName) == true);
        }

        /// <summary>
        /// 根据工具名查找对应的服务绑定
        /// </summary>
        public ServiceBinding? FindBindingByToolName(string deviceToken, string toolName)
        {
            var services = GetServicesByDevice(deviceToken);
            return services.FirstOrDefault(s => s.Tools?.Any(t => t.Name == toolName) == true);
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
        public List<ToolDefinition> Tools { get; set; }

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
        /// 当前连接ID，为空表示离线
        /// </summary>
        public string CurrentConnectionId { get; set; } = string.Empty;

        /// <summary>
        /// 获取绑定的复合键
        /// </summary>
        public string GetBindingKey() => $"binding:{DeviceToken}:{ServiceId}";
    }
}
