using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Middleware;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Middlewares
{
    /// <summary>
    /// 服务器状态中间件，用于处理服务器启动和关闭时的状态信息记录
    /// </summary>
    internal class ServerStatusMiddleware : MiddlewareBase
    {
        private readonly ILogger _logger;

        /// <summary>
        /// 初始化服务器状态中间件实例
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public ServerStatusMiddleware(ILogger<ServerStatusMiddleware> logger)
        {
            this.Order = 1001;
            this._logger = logger;
        }

        /// <summary>
        /// 服务器启动时执行的方法，记录监听地址信息
        /// </summary>
        /// <param name="server">服务器实例</param>
        public override void Start(IServer server)
        {
            ListenOptions? listenOption = server.Options.Listeners.FirstOrDefault();
            if (listenOption is not null)
            {
                // 构建监听URL并记录服务器启动信息
                string listeningUrl = $"{(listenOption.AuthenticationOptions is not null ? "wss://" : "ws://")}{this.GetLocalIP()}:{listenOption.Port}{listenOption.Path}";
                this._logger.LogInformation(Lang.ServerStatusMiddleware_Start_ServerStarted, listeningUrl);
            }
            else
            {
                this._logger.LogWarning(Lang.ServerStatusMiddleware_Start_NoListeningOptions);
            }
        }

        /// <summary>
        /// 服务器关闭时执行的方法，负责资源清理和日志关闭
        /// </summary>
        /// <param name="server">服务器实例</param>
        public override void Shutdown(IServer server)
        {
            // 获取并释放资源管理器
            ResourceManager resourceManager = server.ServiceProvider.GetRequiredService<ResourceManager>();
            resourceManager.Dispose(server.ServiceProvider);

            // 获取并释放提供者管理器
            ProviderManager providerManager = server.ServiceProvider.GetRequiredService<ProviderManager>();
            providerManager.Dispose(server.ServiceProvider);

            this._logger.LogInformation(Lang.ServerStatusMiddleware_Shutdown_ShuttingDown);
            Serilog.Log.CloseAndFlush();
        }

        /// <summary>
        /// 获取本地IP地址
        /// </summary>
        /// <returns>本地IP地址字符串，如果获取失败则返回"127.0.0.1"</returns>
        private string GetLocalIP()
        {
            string hostName = Dns.GetHostName();
            return Dns.GetHostAddresses(hostName).FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
    }
}
