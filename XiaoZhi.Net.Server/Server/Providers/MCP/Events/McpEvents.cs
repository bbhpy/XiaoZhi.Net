using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.Events
{
    /// <summary>
    /// 三方服务绑定成功事件
    /// </summary>
    public record ServiceBoundEvent(
        string DeviceToken,
        string ServiceId,
        string ServiceName,
        DateTime BoundAt
    );

    /// <summary>
    /// 三方服务解绑事件
    /// </summary>
    public record ServiceUnboundEvent(
        string DeviceToken,
        string ServiceId
    );

    /// <summary>
    /// 设备上线事件
    /// </summary>
    public record DeviceOnlineEvent(
        string DeviceToken,
        string SessionId,
        DateTime OnlineAt
    );

    /// <summary>
    /// 设备离线事件
    /// </summary>
    public record DeviceOfflineEvent(
        string DeviceToken,
        DateTime OfflineAt
    );

    /// <summary>
    /// 工具列表更新事件
    /// </summary>
    public record ToolsUpdatedEvent(
        string DeviceToken,
        string ServiceId,
        int ToolCount
    );
}
