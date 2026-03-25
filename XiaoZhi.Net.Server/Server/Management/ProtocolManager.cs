using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Host;
using SuperSocket.WebSocket.Server;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;
using XiaoZhi.Net.Server.Protocol.WebSocket.Handlers;

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
            WebSocketServerOption webSocketOption = config.WebSocketServerOption;

            return builder.ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ProtocolManager>();
                    services.Configure<HandshakeOptions>(opt =>
                    {
                        opt.HandshakeValidator = AuthenticationVerification.VerifyAsync;
                    });
                }).AsWebSocketHostBuilder()
                .ConfigureSuperSocket(options =>
                {
                    options.Name = "Xiao Zhi .Net Server";

                    // 创建两个独立的监听器，分别处理IPv4和IPv6
                    var listeners = new List<ListenOptions>();

                    // IPv4监听器
                    var ipv4Listener = new ListenOptions
                    {
                        Ip = "any",  // 使用 "any" 而不是 "0.0.0.0"
                        Port = webSocketOption.Port,
                        Path = webSocketOption.Path
                    };

                    // IPv6监听器
                    var ipv6Listener = new ListenOptions
                    {
                        Ip = "IpV6Any",  // 使用 "IpV6Any"
                        Port = webSocketOption.Port,
                        Path = webSocketOption.Path
                    };

                    listeners.Add(ipv4Listener);
                    listeners.Add(ipv6Listener);

                    // 配置WSS证书选项（如果使用HTTPS/WSS）
                    if (webSocketOption.WssOption is not null)
                    {
                        var certificate = new X509Certificate2(
                            webSocketOption.WssOption.CertFilePath,
                            webSocketOption.WssOption.CertPassword);

                        foreach (var listener in listeners)
                        {
                            listener.AuthenticationOptions = new ServerAuthenticationOptions
                            {
                                ServerCertificate = certificate
                            };
                        }
                    }
                    options.Listeners = listeners;
                    options.IdleSessionTimeOut = 60;
                    options.ClearIdleSessionInterval = 30;
                })
                .UseWebSocketMessageHandler(MessageDispatch.DispatchAsync)
                .UseServerStatusMonitor()
                .UseXiaoZhiSessionContainer()
                .UseSession<SocketSession>()
                .UseClearIdleSession();
        }
        else
        {
            //MQTT
            throw new NotSupportedException("No MQTT implement yet...");
        }

    }
}
}
