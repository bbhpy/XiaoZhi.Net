using System.Collections.Generic;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// MCP客户端 
    /// </summary>
    internal interface IMcpClient : IProvider<Dictionary<string, MCPClientBuildConfig>>
    {
        /// <summary>
        /// 获取所有子MCP客户端 
        /// </summary>
        /// <returns></returns>
        IDictionary<string, ISubMcpClient> GetAllSubMcpClients();
        /// <summary>
        ///  获取子MCP客户端
        /// </summary>
        /// <param name="subTypeName"></param>
        /// <returns></returns>
        ISubMcpClient? GetSubMcpClient(string subTypeName);
    }
}
