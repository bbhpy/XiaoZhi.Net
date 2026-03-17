namespace XiaoZhi.Net.Server.Common.Constants
{
/// <summary>
/// 定义SubMCP客户端类型的常量名称集合
/// 包含设备IoT客户端、设备MCP客户端、MCP端点客户端和服务端MCP客户端的类型标识符
/// </summary>
internal static class SubMCPClientTypeNames
{
    /// <summary>
    /// 设备IoT客户端类型名称
    /// </summary>
    public const string DeviceIoTClient = "DeviceIoTClient";

    /// <summary>
    /// 设备MCP客户端类型名称
    /// </summary>
    public const string DeviceMcpClient = "DeviceMcpClient";

    /// <summary>
    /// MCP端点客户端类型名称
    /// </summary>
    public const string McpEndpointClient = "McpEndpointClient";

    /// <summary>
    /// 服务端MCP客户端类型名称
    /// </summary>
    public const string ServerMcpClient = "ServerMcpClient";
}
}
