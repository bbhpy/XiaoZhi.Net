using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Host;
using SuperSocket.WebSocket.Server;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Management
{
  /// <summary>
/// 协议管理器类，负责注册和配置服务器协议服务
/// </summary>
internal class ProtocolManager
{
    /// <summary>
    /// 根据配置注册相应的服务器协议服务
    /// </summary>
    /// <param name="builder">主机构建器实例</param>
    /// <param name="config">小智配置对象，包含服务器协议配置信息</param>
    /// <returns>配置完成的主机构建器</returns>
    public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
    {
        if (config.ServerProtocol == ServerProtocol.WebSocket)
        {
                return builder.ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ProtocolManager>();
                    services.AddSingleton<WebSocketServer>();
                    services.AddSingleton<SocketSessionStore>();
                    services.AddHostedService<WebSocketHostedService>();  

                    services.TryAddSingleton<IBasicVerify, SocketBasicVerify>();
                });
            }
        else
        {
            //MQTT
            throw new NotSupportedException("No MQTT implement yet...");
        }

    }
}
}
