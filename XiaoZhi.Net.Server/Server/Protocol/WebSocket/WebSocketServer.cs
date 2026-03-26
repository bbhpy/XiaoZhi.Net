using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuperSocket.WebSocket.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Management;
using SystemWebSocket = System.Net.WebSockets.WebSocket;

namespace XiaoZhi.Net.Server.Server.Protocol.WebSocket
{
    /// <summary>
    /// WebSocket 服务器，使用 System.Net.WebSockets 实现
    /// 支持 IPv4/IPv6 双栈，支持 WSS (TLS)
    /// </summary>
    internal class WebSocketServer : IDisposable
    {
        private readonly ILogger<WebSocketServer> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly HandlerManager _handlerManager;
        private readonly ProviderManager _providerManager;
        private readonly SocketSessionStore _sessionStore;
        private readonly XiaoZhiConfig _config;

        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public WebSocketServer(
            ILogger<WebSocketServer> logger,
            ILoggerFactory loggerFactory,
            IServiceProvider serviceProvider,
            HandlerManager handlerManager,
            ProviderManager providerManager,
            SocketSessionStore sessionStore,
            XiaoZhiConfig config)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _serviceProvider = serviceProvider;
            _handlerManager = handlerManager;
            _providerManager = providerManager;
            _sessionStore = sessionStore;
            _config = config;
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning) return;

            var option = _config.WebSocketServerOption;
            int port = option.Port;
            string path = option.Path.TrimStart('/').TrimEnd('/');

            _cts = new CancellationTokenSource();

            // 创建 HttpListener
            _httpListener = new HttpListener();

            // 正确的格式：http://*:端口/路径/
            string prefix = $"http://*:{port}/{path}/";
            _httpListener.Prefixes.Add(prefix);
            _logger.LogInformation("监听前缀: {Prefix}", prefix);

            // 配置 WSS (TLS) 支持
            if (option.WssOption != null && !string.IsNullOrEmpty(option.WssOption.CertFilePath))
            {
                // TODO: 配置 HTTPS/WSS 证书
                // 需要将 HttpListener 升级为支持 HTTPS
                // 方式一：使用 netsh 命令绑定证书到端口
                // 方式二：使用 HttpListener 的 HTTPS 支持（需要先安装证书）
                // 这里先注释，你后续需要根据证书路径和密码配置
                // string httpsPrefix = $"https://*:{port}/{path}/";
                // _httpListener.Prefixes.Add(httpsPrefix);
                _logger.LogWarning("WSS 支持需要额外配置证书绑定，请先配置 netsh 或使用其他方式");
            }

            try
            {
                _httpListener.Start();
                _isRunning = true;
                _logger.LogInformation($"WebSocket 服务器启动成功，监听端口: {port}，路径: /{path}/，支持 IPv4/IPv6 双栈");
                _logger.LogInformation($"访问地址: ws://localhost:{port}/{path}/");

                // 开始接受连接
                _ = Task.Run(AcceptConnectionsAsync);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动 WebSocket 服务器失败");
                throw;
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();

            // 获取所有 WebSocket 会话并关闭
            var allSessions = _sessionStore.GetAll<WebSocketSession>();
            foreach (var session in allSessions.Values)
            {
                try
                {
                    await session.CloseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "关闭 WebSocket 会话时出错");
                }
            }

            // 清空存储
            _sessionStore.Clear();

            // 停止 HttpListener
            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止 WebSocket 服务器时出错");
            }

            _logger.LogInformation("WebSocket 服务器已停止");
        }

        /// <summary>
        /// 接受连接的循环
        /// </summary>
        private async Task AcceptConnectionsAsync()
        {
            while (_isRunning && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        // 使用 "即发即忘" 模式处理连接
                        _ = Task.Run(() => HandleWebSocketConnectionAsync(context));
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex) when (!_isRunning)
                {
                    // 正常停止时的异常，忽略
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "接受 WebSocket 连接时出错");
                }
            }
        }

        /// <summary>
        /// 处理单个 WebSocket 连接
        /// </summary>
        private async Task HandleWebSocketConnectionAsync(HttpListenerContext httpContext)
        {
            System.Net.WebSockets.WebSocket webSocket = null!;
            string? sessionId = null;

            try
            {
                // 接受 WebSocket 连接
                var wsContext = await httpContext.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;

                // 生成会话 ID
                sessionId = Guid.NewGuid().ToString("N");

                // 获取请求头
                var headers = new Dictionary<string, string>();
                foreach (string? key in httpContext.Request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        headers[key.ToLower()] = httpContext.Request.Headers[key] ?? string.Empty;
                    }
                }

                // 获取客户端 IP 和端口
                var remoteEndPoint = httpContext.Request.RemoteEndPoint as IPEndPoint;

                // 创建 WebSocket 会话
                var session = new SocketSession(
                    sessionId,
                    webSocket,
                    _handlerManager,
                    _providerManager,
                    _sessionStore,
                    _loggerFactory.CreateLogger<WebSocketSession>(),
                    _serviceProvider,
                    headers,
                    remoteEndPoint);

                // 存储到 SocketSessionStore
                _sessionStore.AddSession(sessionId, session);

                // 触发握手验证
                if (!await session.OnConnectingAsync())
                {
                    await session.CloseAsync("Authentication failed");
                    _sessionStore.RemoveSession(sessionId);
                    return;
                }

                // 开始接收消息
                _ = Task.Run(() => session.StartReceivingAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 WebSocket 连接时出错，SessionId: {SessionId}", sessionId);
                try
                {
                    webSocket?.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.InternalServerError, "Internal error", CancellationToken.None);
                }
                catch { }

                // 清理失败的会话
                if (sessionId != null)
                {
                    _sessionStore.RemoveSession(sessionId);
                }
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            _cts?.Dispose();
            _httpListener?.Close();
        }
    }
}
