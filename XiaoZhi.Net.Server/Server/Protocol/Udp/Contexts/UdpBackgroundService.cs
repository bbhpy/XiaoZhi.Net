using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP 后台监听服务
    /// 对标 SuperSocket WebSocket 监听主机
    /// 使用原生 UdpClient 实现
    /// </summary>
    internal class UdpBackgroundService : BackgroundService
    {
        private readonly UdpClient _udpClient;
        private readonly MqttUdpSessionStore _mqttUdpSessionStore;
        private readonly UdpMessageDispatch _messageDispatch;
        private readonly XiaoZhiConfig _config;
        private readonly ILogger<UdpBackgroundService> _logger;
        public UdpBackgroundService(
            MqttUdpSessionStore sessionManager,
            UdpMessageDispatch messageDispatch,
            XiaoZhiConfig config,
            ILogger<UdpBackgroundService> logger,
            UdpClient udpClient)
        {
            _mqttUdpSessionStore = sessionManager;
            _messageDispatch = messageDispatch;
            _config = config;
            _logger = logger;
            _udpClient = udpClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UDP后台服务已启动，监听端口：{Port}", _config.UdpConfig.Port);

            stoppingToken.Register(() =>
            {
                _logger.LogInformation("UDP后台服务正在停止...");
                _udpClient.Close();
                _udpClient.Dispose();
                _logger.LogInformation("UDP后台服务已停止");
            });

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    UdpReceiveResult receiveResult;
                    byte[] data;
                    IPEndPoint clientEP;

                    try
                    {
                        // 使用接收专用客户端接收数据
                        receiveResult = await _udpClient.ReceiveAsync(stoppingToken);

                        if (receiveResult.Buffer == null || receiveResult.Buffer.Length == 0)
                        {
                            _logger.LogWarning("收到空的UDP数据包，远端地址：{RemoteEndPoint}", receiveResult.RemoteEndPoint);
                            continue;
                        }

                        data = receiveResult.Buffer;
                        clientEP = receiveResult.RemoteEndPoint;

                        if (!UdpAudioPacket.TryParse(data, out var packet))
                        {
                            string bufferHex = BitConverter.ToString(data).Replace("-", "");
                            string bufferText = Encoding.UTF8.GetString(data);
                            _logger.LogWarning(
                                "UDP数据包格式错误，客户端：{ClientEP}，数据包长度：{Length}字节，十六进制内容：{BufferHex}，UTF8文本：{BufferText}",
                                clientEP, data.Length, bufferHex, bufferText
                            );
                            continue;
                        }

                        var udpSession = _mqttUdpSessionStore.GetSessionBySsrc(packet.Ssrc);
                        if (udpSession != null)
                        {
                            udpSession.UpdateUdpRemoteEndPoint((clientEP));
                            udpSession.RefreshLastActivityTime();

                            byte[] bytes = AesKeyGenerator.DecryptUdpAudioPayload(
                                packet.Payload,
                                udpSession.UdpAesNonce,
                                packet.PayloadLength,
                                packet.Timestamp,
                                packet.Sequence,
                                udpSession.UdpAesKey);

                            if (udpSession.XiaoZhiSession != null)
                            {
                                await udpSession.XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(bytes);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("UDP接收操作已取消");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogInformation("UDP客户端已释放，停止接收");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "UDP接收数据时发生异常");
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "UDP后台服务主循环发生致命异常");
                throw;
            }
            finally
            {
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
            }

            _logger.LogInformation("UDP后台服务执行完成");
        }
        // 检查地址类型
        /// <summary>
        /// 获取实际的 IPEndPoint（如果是 IPv4 映射地址则转为纯 IPv4）
        /// </summary>
        /// <param name="endpoint">原始端点</param>
        /// <returns>实际的端点（IPv4 映射地址会转为 IPv4）</returns>
        public IPEndPoint GetActualIPEndPoint(IPEndPoint endpoint)
        {
            if (endpoint == null) return null;

            // 检查是否为 IPv4 映射到 IPv6 的地址
            if (endpoint.Address.IsIPv4MappedToIPv6)
            {
                // 转换为纯 IPv4 地址
                IPAddress actualAddress = endpoint.Address.MapToIPv4();
                // 返回新的 IPEndPoint（端口保持不变）
                return new IPEndPoint(actualAddress, endpoint.Port);
            }

            // 纯 IPv4 或纯 IPv6 直接返回原对象
            return endpoint;
        }
        /// <summary>
        /// 通用UDP下发函数（每次发送创建新的发送客户端）
        /// </summary>
        public async Task<bool> SendUdpMessageAsync(IPEndPoint targetEP, byte[] data, CancellationToken cancellationToken = default)
        {
            if (targetEP == null)
            {
                _logger.LogWarning("UDP下发失败：目标地址为空");
                return false;
            }

            if (data == null || data.Length == 0)
            {
                _logger.LogWarning("UDP下发失败：发送数据为空，目标地址={RemoteEP}", targetEP);
                return false;
            }

            try
            {
                await _udpClient.SendAsync(data, data.Length, targetEP);
                //_logger.LogDebug("UDP下发成功：目标地址={RemoteEP}，数据长度={Length}字节", targetEP, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP下发失败：目标地址={RemoteEP}", targetEP);
                return false;
            }
        }
    }
}