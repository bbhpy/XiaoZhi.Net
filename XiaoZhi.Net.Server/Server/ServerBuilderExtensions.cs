using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.ObjectPoolPolicies;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;

namespace XiaoZhi.Net.Server
{
    /// <summary>
    /// 服务器构建器扩展类，提供各种服务注册的扩展方法
    /// </summary>
    internal static class ServerBuilderExtensions
    {
        /// <summary>
        /// 注册日志服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器实例</param>
        /// <param name="config">小知配置对象</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterLogger(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return LoggerManager.RegisterServices(builder, config);
        }

        /// <summary>
        /// 注册资源管理服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器实例</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterResources(this IHostBuilder builder)
        {
            return ResourceManager.RegisterServices(builder);
        }

        /// <summary>
        /// 注册提供者服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器实例</param>
        /// <param name="config">小知配置对象</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterProviders(this IHostBuilder builder, XiaoZhiConfig config)
        {
            return ProviderManager.RegisterServices(builder, config);
        }

        /// <summary>
        /// 注册处理器服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器实例</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterHandlers(this IHostBuilder builder)
        {
            return HandlerManager.RegisterServices(builder);
        }

        /// <summary>
        /// 注册协议服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器实例</param>
        /// <param name="config">小知配置对象</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterProtocol(this IHostBuilder builder, XiaoZhiConfig config)
        {
            // 先注册原有协议逻辑
            builder = ProtocolManager.RegisterServices(builder, config);

            // 新增：注册MQTT协议
            builder = builder.AddMqttUdpProtocol(config);

            builder = builder.AddMCPProtocol(config);

            return builder;
        }
        /// <summary>
        /// 注册对象池服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器实例</param>
        /// <returns>配置后的主机构建器</returns>
        public static IHostBuilder RegisterObjectPools(this IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                // 注册对象池提供者
                services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();

                // 注册 OutSegment 对象池
                services.AddSingleton<ObjectPool<OutSegment>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new OutSegmentPolicy();
                    return provider.Create(policy);
                });

                // 注册 OutAudioSegment 对象池
                services.AddSingleton<ObjectPool<OutAudioSegment>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new OutAudioSegmentPolicy();
                    return provider.Create(policy);
                });

                // 注册 Workflow<OutAudioSegment> 对象池
                services.AddSingleton<ObjectPool<Workflow<OutAudioSegment>>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new WorkflowPolicy<OutAudioSegment>();
                    return provider.Create(policy);
                });

                // 注册 MixedAudioPacket 对象池
                services.AddSingleton<ObjectPool<MixedAudioPacket>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new MixedAudioPacketPolicy();
                    return provider.Create(policy);
                });

                // 注册 Workflow<MixedAudioPacket> 对象池
                services.AddSingleton<ObjectPool<Workflow<MixedAudioPacket>>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new WorkflowPolicy<MixedAudioPacket>();
                    return provider.Create(policy);
                });

                // 注册 Workflow<string> 对象池
                services.AddSingleton<ObjectPool<Workflow<string>>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new WorkflowPolicy<string>();
                    return provider.Create(policy);
                });

                // 注册 Workflow<float[]> 对象池
                services.AddSingleton<ObjectPool<Workflow<float[]>>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new WorkflowPolicy<float[]>();
                    return provider.Create(policy);
                });

                // 注册 Workflow<OutSegment> 对象池
                services.AddSingleton<ObjectPool<Workflow<OutSegment>>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new WorkflowPolicy<OutSegment>();
                    return provider.Create(policy);
                });

                // 注册 Workflow<DialogueContext> 对象池
                services.AddSingleton<ObjectPool<Workflow<DialogueContext>>>(serviceProvider =>
                {
                    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                    var policy = new WorkflowPolicy<DialogueContext>();
                    return provider.Create(policy);
                });
            });
        }
    }
}
