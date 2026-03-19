using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP
{
    /// <summary>
    /// MCP服务端端点接口，负责监听和接受三方MCP服务的连接
    /// </summary>
    internal interface IMcpServerEndpoint : IDisposable
    {
        /// <summary>
        /// 启动服务端，开始监听端口
        /// </summary>
        /// <param name="port">监听端口</param>
        /// <param name="path">WebSocket路径，默认"/mcp"</param>
        /// <returns>启动是否成功</returns>
        Task<bool> StartAsync(int port, string path = "/mcp");

        /// <summary>
        /// 停止服务端
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 当前是否在运行
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 当前活跃的连接数
        /// </summary>
        int ActiveConnections { get; }
    }
}
