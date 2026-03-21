using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace XiaoZhi.Net.Server.Server.Providers.MCP.ServerEndpoint
{
    /// <summary>
    /// MCP服务端端点，监听端口接受三方MCP服务的WebSocket连接
    /// </summary>
    internal class McpServerEndpoint : IMcpServerEndpoint
    {
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private readonly ILogger<McpServerEndpoint> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly McpServiceStore _serviceStore;
        private readonly ToolRegistry _toolRegistry;  // 新增
        private readonly TokenSessionRegistry _tokenRegistry;
        private readonly ConcurrentDictionary<string, McpServerConnection> _connections = new();
        private readonly ThirdPartyToolRegistrar _toolRegistrar;

        private int _port;
        private string _path = "/mcp";
        private bool _isRunning;

        public bool IsRunning => _isRunning;
        public int ActiveConnections => _connections.Count;
        public McpServerEndpoint(
                ILogger<McpServerEndpoint> logger,
                IServiceProvider serviceProvider,
                McpServiceStore serviceStore,
                ToolRegistry toolRegistry,
                TokenSessionRegistry tokenRegistry) 
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _serviceStore = serviceStore;
            _toolRegistry = toolRegistry;
            _tokenRegistry = tokenRegistry;
        }

        /// <summary>
        /// 启动服务端，开始监听端口
        /// </summary>
        public async Task<bool> StartAsync(int port, string path = "/mcp")
        {
            try
            {
                _port = port;
                _path = path.TrimStart('/');
                _cts = new CancellationTokenSource();

                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://*:{_port}/{_path}/");
                _httpListener.Start();

                _isRunning = true;
                _logger.LogInformation("MCP ServerEndpoint started on port {Port}, path: /{Path}", _port, _path);

                // 开始接受连接
                _ = Task.Run(AcceptConnectionsAsync);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start MCP ServerEndpoint on port {Port}", port);
                return false;
            }
        }

        /// <summary>
        /// 停止服务端
        /// </summary>
        public async Task StopAsync()
        {
            _isRunning = false;
            _cts?.Cancel();

            // 关闭所有连接
            foreach (var conn in _connections.Values)
            {
                await conn.CloseAsync("Server shutting down");
            }
            _connections.Clear();

            _httpListener?.Stop();
            _httpListener?.Close();

            _logger.LogInformation("MCP ServerEndpoint stopped");
        }

        /// <summary>
        /// 接受连接的循环
        /// </summary>
        private async Task AcceptConnectionsAsync()
        {
            while (_isRunning && _httpListener != null && !_cts!.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();

                    // 只处理WebSocket请求
                    if (context.Request.IsWebSocketRequest)
                    {
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
                    // 正常关闭
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting connection");
                }
            }
        }

        /// <summary>
        /// 处理单个WebSocket连接
        /// </summary>
        private async Task HandleWebSocketConnectionAsync(HttpListenerContext context)
        {
            WebSocketContext? wsContext = null;
            try
            {
                // 从URL中解析token参数
                var queryString = context.Request.Url?.Query;
                string? deviceToken = null;

                if (!string.IsNullOrEmpty(queryString))
                {
                    var queryParams = HttpUtility.ParseQueryString(queryString);
                    deviceToken = queryParams["token"];
                }

                // 1. 验证token
                if (string.IsNullOrEmpty(deviceToken))
                {
                    _logger.LogWarning("Connection without token from {RemoteEndPoint}, rejected",
                        context.Request.RemoteEndPoint);
                    context.Response.StatusCode = 401;
                    context.Response.StatusDescription = "Missing token";
                    context.Response.Close();
                    return;
                }

                // 2. 验证token是否有效
                if (!_tokenRegistry.ValidateToken(deviceToken))
                {
                    _logger.LogWarning("Invalid token {Token} from {RemoteEndPoint}, rejected",
                        deviceToken, context.Request.RemoteEndPoint);
                    context.Response.StatusCode = 403;
                    context.Response.StatusDescription = "Invalid token";
                    context.Response.Close();
                    return;
                }

                // token有效，才接受WebSocket连接
                wsContext = await context.AcceptWebSocketAsync(null);
                _logger.LogInformation("New WebSocket connection from {RemoteEndPoint} with valid token: {Token}",
                    context.Request.RemoteEndPoint, deviceToken);

                // 创建连接实例处理，传入验证好的token和ToolRegistry
                var connection = ActivatorUtilities.CreateInstance<McpServerConnection>(
                    _serviceProvider,
                    wsContext.WebSocket,
                    _serviceStore,
                    _toolRegistry,  // 传入ToolRegistry
                    deviceToken);

                var connectionId = Guid.NewGuid().ToString("N");
                _connections[connectionId] = connection;

                // 处理连接（这里会等待直到连接关闭）
                await connection.HandleAsync();

                // 清理
                _connections.TryRemove(connectionId, out _);
                _logger.LogDebug("Connection {ConnectionId} removed, active connections: {Count}",
                    connectionId, _connections.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WebSocket connection");
                wsContext?.WebSocket?.Dispose();
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
