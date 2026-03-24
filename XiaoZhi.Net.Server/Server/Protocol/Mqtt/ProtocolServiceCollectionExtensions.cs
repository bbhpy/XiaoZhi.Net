using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Server.Internal;
using Serilog;
using SuperSocket.Server.Abstractions.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.MCP;
using XiaoZhi.Net.Server.Providers.MCP.DeviceMcp;
using XiaoZhi.Net.Server.Providers.MCP.McpEndpoint;
using XiaoZhi.Net.Server.Providers.MCP.ServerMcp;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts;
using XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts;
using XiaoZhi.Net.Server.Server.Providers.MCP;
using XiaoZhi.Net.Server.Server.Providers.MCP.Events;
using XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt
{
    /// <summary>
    /// MQTT + UDP 服务注册扩展
    /// 对标 WebSocket 注册 UseWebSocket 等
    /// </summary>
    public static class ProtocolServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 MQTT 全套服务
        /// </summary>
        public static IHostBuilder AddMqttUdpProtocol(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<Serilog.ILogger>(Log.Logger);
                // ========== 1. 单例服务（全局唯一） ==========
                // 会话存储（MQTT+UDP）
                //services.AddSingleton<MqttUdpSessionStore>();
                // 适配IStore接口（如果原有代码需要）
                services.AddSingleton<IStore>(sp => sp.GetRequiredService<MqttUdpSessionStore>());
                // MQTT核心服务（单例，全局一个MQTT服务端）
                services.AddSingleton<MqttService>();

                // ========== 2. UDP 接收客户端（单例，长期监听） ==========
                services.AddSingleton<UdpClient>(sp=>
                {
                    var xiaoZhiConfig = sp.GetRequiredService<XiaoZhiConfig>();
                    var logger = sp.GetRequiredService<Serilog.ILogger>();

                    int udpPort = xiaoZhiConfig.UdpConfig.Port > 0 ? xiaoZhiConfig.UdpConfig.Port : 8888;

                    try
                    {
                        // 创建 IPv6 双栈 Socket，支持 IPv4 和 IPv6 客户端
                        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                        // 关键：禁用 IPv6Only，启用双栈（同时监听 IPv4 和 IPv6）
                        socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, udpPort));

                        var udpClient = new UdpClient();
                        udpClient.Client = socket;

                        logger.Information("UDP接收服务（单例）启动成功，监听端口：{Port}，双栈模式（IPv4+IPv6）", udpPort);
                        return udpClient;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "创建UDP接收客户端(IPv6双栈)失败，尝试使用IPv4模式");
                        // 回退到 IPv4
                        var udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, udpPort));
                        logger.Information("UDP接收服务启动IPv4模式，监听端口：{Port}", udpPort);
                        return udpClient;
                    }
                });

                // ========== 2. 瞬态服务（每次请求/new） ==========
                // MQTT会话（每个客户端连接创建一个新实例）
                services.AddTransient<MqttUdpSession>();

                // 1. 注册【统一】的MQTT+UDP合并会话存储（核心修改：作为底层存储）
                services.AddSingleton<MqttUdpSessionStore>();

                // 3. 注册MqttUdpSendOutter（瞬态，每次使用新建，关联会话+MQTT/UDP客户端）
                services.AddTransient<MqttUdpSendOutter>(sp =>
                {
                    var mqttClient = sp.GetRequiredService<IMqttClient>();
                    var senderUdpClient = sp.GetRequiredKeyedService<UdpClient>("sender"); // 使用发送专用客户端
                    var logger = sp.GetRequiredService<ILogger<MqttUdpSendOutter>>();

                    return new MqttUdpSendOutter(
                        session: null,
                        mqttClient: mqttClient,
                        senderUdpClient: senderUdpClient,  // 注入发送客户端
                        logger: logger);
                });

                // ========== 3. 配置MQTT服务（启动时初始化） ==========
                services.AddHostedService<MqttHostedService>();

                services.AddSingleton<UdpMessageDispatch>();

                // 4. 注册 UDP Worker 池（单例 + 后台服务）
                services.AddSingleton<UdpWorkerPool>();
                services.AddHostedService(provider => provider.GetRequiredService<UdpWorkerPool>());

                // 5. 注册 UDP 后台监听服务（后台服务，监听 UDP 端口收包）
                services.AddSingleton<UdpBackgroundService>();
                services.AddHostedService(provider => provider.GetRequiredService<UdpBackgroundService>());

              

            });
        }

        public static IHostBuilder AddMCPProtocol(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
                // 1. 注册事件系统
                services.AddSingleton<IEventPublisher, EventPublisher>();

                // 2. 注册数据存储（重写后的）
                services.AddSingleton<McpServiceStore>();

                // 3. 注册路由层组件
                services.AddSingleton<ToolRouter>();
                services.AddSingleton<McpConnectionManager>();
                services.AddSingleton<McpCallManager>();

                // 4. 注册 Token 注册表
                services.AddSingleton<TokenSessionRegistry>();

                // 5. 注册业务逻辑层
                services.AddSingleton<ThirdPartyToolRegistrar>();
                services.AddSingleton<DeviceOnlineHandler>();

                // 6. 注册 MCP 服务器端点
                services.AddSingleton<McpServerEndpoint>();
                services.AddSingleton<McpToolInvoker>();
                // ⭐ 5. 注册子客户端
                services.AddKeyedTransient<ISubMcpClient, DeviceMcpClient>(SubMCPClientTypeNames.DeviceMcpClient);
                services.AddKeyedTransient<ISubMcpClient, McpEndpointClient>(SubMCPClientTypeNames.McpEndpointClient);
                services.AddKeyedTransient<ISubMcpClient, ServerMcpClient>(SubMCPClientTypeNames.ServerMcpClient);
                services.AddTransient<IMcpClient, McpClient>();

                // ⭐ 6. 最后注册后台服务
                services.AddHostedService(sp => new McpServerHostedService(
                    sp.GetRequiredService<McpServerEndpoint>(),
                    sp.GetRequiredService<ILogger<McpServerHostedService>>(),
                    config.McpServerEndpointConfig.Port,
                    config.McpServerEndpointConfig.Path
                ));
            });
        }
    }
}
