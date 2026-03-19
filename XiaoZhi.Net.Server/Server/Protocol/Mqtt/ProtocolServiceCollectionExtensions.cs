using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Server.Internal;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts;
using XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts;
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

                // ========== 2. 瞬态服务（每次请求/new） ==========
                // MQTT会话（每个客户端连接创建一个新实例）
                services.AddTransient<MqttUdpSession>();

                // 1. 注册【统一】的MQTT+UDP合并会话存储（核心修改：作为底层存储）
                services.AddSingleton<MqttUdpSessionStore>();

                // 2. 注册UdpSessionManager（核心修改：注入统一存储+原有依赖）
                services.AddSingleton<UdpSessionManager>(sp =>
                {
                    var sessionStore = sp.GetRequiredService<MqttUdpSessionStore>();
                    var providerManager = sp.GetRequiredService<ProviderManager>(); // 原有依赖
                    var logger = sp.GetRequiredService<ILogger<UdpSessionManager>>();
                    return new UdpSessionManager(sessionStore, providerManager, logger);
                });

                // 3. 注册MqttUdpSendOutter（瞬态，每次使用新建，关联会话+MQTT/UDP客户端）
                services.AddTransient<MqttUdpSendOutter>(sp =>
                {
                    // 注意：SendOutter依赖具体的MqttUdpCompositeSession，此处仅注册类型，
                    // 实际创建需结合会话实例（建议在UdpSessionManager中按需创建）
                    // 若IMqttClient未注册，需先补充注册：services.AddSingleton<IMqttClient>(sp => new MqttClient());
                    var mqttClient = sp.GetRequiredService<IMqttClient>();
                    var udpClient = sp.GetRequiredService<UdpClient>();
                    var logger = sp.GetRequiredService<ILogger<MqttUdpSendOutter>>();

                    // 临时占位（实际使用时需传入具体的MqttUdpCompositeSession）
                    // 推荐：在UdpSessionManager中创建会话时，为会话绑定SendOutter
                    return new MqttUdpSendOutter(
                        session: null, // 实际使用时替换为具体会话
                        mqttClient: mqttClient,
                        udpClient: udpClient,
                        logger: logger);
                });

                // 4. 注册UDP消息分发器（单例，依赖UdpSessionManager）
                services.AddSingleton<UdpMessageDispatch>();

                // 5. 注册UDP鉴权校验器（单例，鉴权结果写入统一会话）
                services.AddSingleton<UdpAuthenticationVerification>();

                // ========== 3. 配置MQTT服务（启动时初始化） ==========
                services.AddHostedService<MqttHostedService>(); // 后台服务启动MQTT

                // 6. 注册UDP后台监听服务（后台服务，监听UDP端口收包）
                services.AddSingleton<UdpBackgroundService>();
                services.AddHostedService(provider => provider.GetRequiredService<UdpBackgroundService>());

                // 7. 注册UDP会话超时清理中间件（后台服务，依赖统一存储）
                services.AddHostedService<UdpSessionTimeoutMiddleware>(sp =>
                {
                    var xiaoZhiConfig = sp.GetRequiredService<XiaoZhiConfig>();
                    var logger = sp.GetRequiredService<ILogger<UdpSessionTimeoutMiddleware>>();
                    var sessionStore = sp.GetRequiredService<MqttUdpSessionStore>(); // 依赖统一存储

                    // 从配置读取超时时间，默认60秒
                    int timeoutSeconds = xiaoZhiConfig.UdpConfig.UdpSessionTimeoutSeconds > 0
                        ? xiaoZhiConfig.UdpConfig.UdpSessionTimeoutSeconds
                        : 60;

                    // 初始化超时中间件，传入统一存储
                    return new UdpSessionTimeoutMiddleware(sessionStore, logger)
                    {
                        SessionTimeoutSeconds = timeoutSeconds
                    };
                });

                // 8. 注册原生UdpClient实例（单例，绑定配置的端口）
                services.AddSingleton<UdpClient>(sp =>
                {
                    var xiaoZhiConfig = sp.GetRequiredService<XiaoZhiConfig>();
                    // 从配置读取UDP端口，默认8888
                    int udpPort = xiaoZhiConfig.UdpConfig.Port > 0 ? xiaoZhiConfig.UdpConfig.Port : 8888;
                    // 绑定所有网卡的指定端口，支持多设备接入
                    return new UdpClient(new IPEndPoint(IPAddress.Any, udpPort));
                });

            });
        }

        public static IHostBuilder AddMCPProtocol(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureServices((context, services) =>
            {
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
