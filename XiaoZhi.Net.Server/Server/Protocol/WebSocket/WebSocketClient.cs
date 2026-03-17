using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    /// <summary>
    /// WebSocket客户端类，用于处理WebSocket连接、消息收发等操作
    /// </summary>
    internal class WebSocketClient : IDisposable
    {
        private WebsocketClient? _socket;
        private readonly SemaphoreSlim _socketSemaphore = new(1, 1);

        /// <summary>
        /// 获取当前WebSocket连接状态
        /// </summary>
        public bool IsConnected => this._socket?.IsRunning ?? false;
        private readonly IDictionary<string, string>? _headers;

        /// <summary>
        /// 初始化WebSocketClient实例
        /// </summary>
        /// <param name="headers">可选的请求头字典</param>
        public WebSocketClient(IDictionary<string, string>? headers)
        {
            this._headers = headers;
        }

        /// <summary>
        /// 获取或设置端点URL
        /// </summary>
        public Uri? EndpointUrl { get; private set; }

        #region Events

        /// <summary>
        /// 连接打开事件
        /// </summary>
        public event Action? OnOpen;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event Action<WebSocketError, string>? OnError;

        /// <summary>
        /// 连接关闭事件
        /// </summary>
        public event Action<WebSocketCloseStatus?, string?>? OnClose;

        /// <summary>
        /// 文本消息接收事件
        /// </summary>
        public event Action<string>? OnTextMessage;

        /// <summary>
        /// 二进制消息接收事件
        /// </summary>
        public event Action<byte[]>? OnBinaryMessage;

        #endregion

        /// <summary>
        /// 异步连接到指定的WebSocket端点
        /// </summary>
        /// <param name="endpointUrl">WebSocket端点URL</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task ConnectAsync(string endpointUrl, CancellationToken cancellationToken = default)
        {
            this.EndpointUrl = new Uri(endpointUrl);

            try
            {
                await this._socketSemaphore.WaitAsync(cancellationToken);

                if (this._socket is not null)
                {
                    this._socket.Dispose();
                    this._socket = null;
                }

                if (this._socket is null)
                {
                    // 创建新的WebSocket客户端实例并配置选项
                    this._socket = new WebsocketClient(this.EndpointUrl, () =>
                    {
                        ClientWebSocket socket = new ClientWebSocket();
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        socket.Options.CollectHttpResponseDetails = true;
                        if (this._headers is not null)
                            foreach (var item in this._headers)
                            {
                                socket.Options.SetRequestHeader(item.Key, item.Value);
                            }
                        return socket;
                    })
                    {
                        IsReconnectionEnabled = false,
                        LostReconnectTimeout = null,
                        ErrorReconnectTimeout = null,
                        ReconnectTimeout = null
                    };

                    // 订阅文本消息接收事件
                    this._socket.MessageReceived
                        .Where(msg => msg.MessageType == WebSocketMessageType.Text)
                        .Where(msg => !string.IsNullOrEmpty(msg.Text))
                        .Subscribe(msg => this.OnTextMessage?.Invoke(msg.Text!));

                    // 订阅二进制消息接收事件
                    this._socket.MessageReceived
                         .Where(msg => msg.MessageType == WebSocketMessageType.Binary)
                         .Where(msg => msg.Binary is not null)
                         .Subscribe(msg => this.OnBinaryMessage?.Invoke(msg.Binary!));

                    // 订阅重连事件（当前为空实现）
                    this._socket.ReconnectionHappened
                        .Subscribe(e =>
                        {
                            //Console.WriteLine("ReconnectionHappened: " + e.Type);
                        });

                    // 订阅断开连接事件
                    this._socket.DisconnectionHappened
                        .Subscribe(e =>
                        {
                            e.CancelReconnection = true;
                            this.OnClose?.Invoke(e.CloseStatus, e.CloseStatusDescription);

                        });
                }

                await this._socket.StartOrFail();

                this.OnOpen?.Invoke();
            }
            catch (Exception ex)
            {
                this.OnError?.Invoke(WebSocketError.ConnectionClosedPrematurely, ex.Message);
                return;
            }
            finally
            {
                this._socketSemaphore.Release();
            }
        }

        /// <summary>
        /// 异步发送文本消息
        /// </summary>
        /// <param name="text">要发送的文本消息</param>
        /// <returns>完成的任务</returns>
        public Task SendAsync(string text)
        {
            this._socket?.Send(text);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 异步发送二进制数据
        /// </summary>
        /// <param name="data">要发送的二进制数据</param>
        /// <returns>完成的任务</returns>
        public Task SendAsync(byte[] data)
        {
            this._socket?.Send(data);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 异步关闭WebSocket连接
        /// </summary>
        /// <param name="webSocketCloseStatus">关闭状态码</param>
        /// <param name="statusDescription">关闭状态描述</param>
        /// <returns>异步任务</returns>
        public async Task CloseAsync(WebSocketCloseStatus webSocketCloseStatus = WebSocketCloseStatus.Empty, string statusDescription = "")
        {
            if (!this.IsConnected)
            {
                return;
            }
            try
            {
                this.OnClose?.Invoke(webSocketCloseStatus, statusDescription);
                if (this._socket is not null)
                {
                    await this._socket.StopOrFail(webSocketCloseStatus, statusDescription);
                }
            }
            catch (Exception)
            {
                this.OnError?.Invoke(WebSocketError.ConnectionClosedPrematurely, Lang.WebSocketClient_CloseAsync_CloseFailed);
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            this._socket?.Dispose();
        }
    }
}
