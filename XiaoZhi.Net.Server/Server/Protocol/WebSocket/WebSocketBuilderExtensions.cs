using Microsoft.Extensions.DependencyInjection;
using SuperSocket.Server;
using SuperSocket.Server.Abstractions.Host;
using SuperSocket.Server.Abstractions.Session;
using XiaoZhi.Net.Server.Protocol.WebSocket.Middlewares;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    /// <summary>
    /// WebSocket构建器扩展类，提供服务器状态监控和会话容器功能的配置扩展方法
    /// </summary>
    internal static class WebSocketBuilderExtensions
    {
        /// <summary>
        /// 为SuperSocket主机构建器启用服务器状态监控中间件
        /// </summary>
        /// <typeparam name="TReceivePackage">接收包的数据类型</typeparam>
        /// <param name="builder">SuperSocket主机构建器实例</param>
        /// <returns>配置后的SuperSocket主机构建器</returns>
        public static ISuperSocketHostBuilder<TReceivePackage> UseServerStatusMonitor<TReceivePackage>(this ISuperSocketHostBuilder<TReceivePackage> builder)
        {
            return builder.UseMiddleware<ServerStatusMiddleware>();
        }

        /// <summary>
        /// 为SuperSocket主机构建器启用小智会话容器功能
        /// 配置会话容器中间件并注册相关服务（会话容器、异步会话容器等）
        /// </summary>
        /// <typeparam name="TReceivePackage">接收包的数据类型</typeparam>
        /// <param name="builder">SuperSocket主机构建器实例</param>
        /// <returns>配置后的SuperSocket主机构建器</returns>
        public static ISuperSocketHostBuilder<TReceivePackage> UseXiaoZhiSessionContainer<TReceivePackage>(this ISuperSocketHostBuilder<TReceivePackage> builder)
        {
            return (builder
                .UseMiddleware<SessionContainerMiddleware>(s => s.GetRequiredService<SessionContainerMiddleware>())
                .ConfigureServices((context, services) =>
                {
                    // 注册会话容器中间件及相关服务到依赖注入容器
                    services.AddSingleton<SessionContainerMiddleware>();
                    services.AddSingleton<ISessionContainer>((s) => s.GetRequiredService<SessionContainerMiddleware>());
                    services.AddSingleton<IAsyncSessionContainer>((s) => s.GetRequiredService<ISessionContainer>().ToAsyncSessionContainer());
                }) as ISuperSocketHostBuilder<TReceivePackage>)!;
        }
    }
}
