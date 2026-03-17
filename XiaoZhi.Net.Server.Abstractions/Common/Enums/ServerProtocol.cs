using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    /// <summary>
    /// 服务器协议
    /// </summary>
    public enum ServerProtocol
    {
        [Description("WebSocket")]
        WebSocket,
        [Description("Mqtt")]
        Mqtt,
        [Description("Udp")]
        Udp,
    }
}
