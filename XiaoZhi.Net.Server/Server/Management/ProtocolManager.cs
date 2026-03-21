using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Host;
using SuperSocket.WebSocket.Server;
using System;
using System.Collections.Generic;
using System.Net;
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

                    ListenOptions listenOptions = new ListenOptions
                    {
                        Ip = "IpV6Any",
                        Port = webSocketOption.Port,
                        Path = webSocketOption.Path
                    };

                    // 配置WSS证书选项
                    if (webSocketOption.WssOption is not null)
                    {
                        listenOptions.AuthenticationOptions.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(webSocketOption.WssOption.CertFilePath, webSocketOption.WssOption.CertPassword);
                    }
                    options.Listeners = new List<ListenOptions> { listenOptions };
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
