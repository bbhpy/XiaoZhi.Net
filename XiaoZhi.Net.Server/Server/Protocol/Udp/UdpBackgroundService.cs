using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp
{
    /// <summary>
    /// UDP 后台监听服务
    /// 对标 SuperSocket WebSocket 监听主机
    /// 使用原生 UdpClient 实现
    /// 职责：接收 UDP 数据包，快速解析 SSRC，入队到 Worker 池
    /// 不执行任何耗时操作（解密、解析、业务处理）
    /// </summary>
    internal class UdpBackgroundService : BackgroundService
    {
        private readonly UdpClient _udpClient;
        private readonly UdpWorkerPool _workerPool;
        private readonly XiaoZhiConfig _config;
        private readonly ILogger<UdpBackgroundService> _logger;

        public UdpBackgroundService(
            UdpClient udpClient,
            UdpWorkerPool workerPool,
            XiaoZhiConfig config,
            ILogger<UdpBackgroundService> logger)
        {
            _udpClient = udpClient;
            _workerPool = workerPool;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UDP 后台服务已启动，监听端口：{Port}", _config.UdpConfig.Port);

            stoppingToken.Register(() =>
            {
                _logger.LogInformation("UDP 后台服务正在停止...");
                _udpClient.Close();
                _udpClient.Dispose();
                _logger.LogInformation("UDP 后台服务已停止");
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
                        receiveResult = await _udpClient.ReceiveAsync(stoppingToken).ConfigureAwait(false);

                        if (receiveResult.Buffer == null || receiveResult.Buffer.Length == 0)
                        {
                            _logger.LogWarning("收到空的 UDP 数据包，远端地址：{RemoteEndPoint}", receiveResult.RemoteEndPoint);
                            continue;
                        }

                        data = receiveResult.Buffer;
                        clientEP = receiveResult.RemoteEndPoint;

                        // 快速解析 SSRC（仅用于路由，不做完整校验）
                        // UDP 包格式：Type(1) + Flags(1) + PayloadLen(2) + SSRC(4) + ...
                        // SSRC 位于偏移 4，长度为 4 字节，网络字节序
                        uint ssrc = 0;
                        if (data.Length >= 8)
                        {
                            ssrc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
                        }
                        else
                        {
                            // 包长度不足，无法读取 SSRC，直接丢弃
                            _logger.LogWarning(
                                "UDP 数据包长度不足，无法读取 SSRC，远端={RemoteEP}，长度={Length}",
                                clientEP, data.Length);
                            continue;
                        }

                        // 创建工作项
                        var workItem = new UdpWorkItem
                        {
                            RawData = data,
                            Ssrc = ssrc,
                            RemoteEndPoint = clientEP,
                            ReceivedTime = DateTime.UtcNow
                        };

                        // 入队到 Worker 池（非阻塞，按 SSRC 哈希路由）
                        await _workerPool.EnqueueAsync(workItem, stoppingToken).ConfigureAwait(false);

                        // 可选：记录高负载日志（队列深度监控）
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            var (_, queueLengths) = _workerPool.GetStatus();
                            // 仅在队列长度超过阈值时记录（避免日志刷屏）
                            // 此处暂不实现，可按需添加
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("UDP 接收操作已取消");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogInformation("UDP 客户端已释放，停止接收");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "UDP 接收数据时发生异常");
                        await Task.Delay(100, stoppingToken).ConfigureAwait(false);
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "UDP 后台服务主循环发生致命异常");
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

            _logger.LogInformation("UDP 后台服务执行完成");
        }

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
                IPAddress actualAddress = endpoint.Address.MapToIPv4();
                return new IPEndPoint(actualAddress, endpoint.Port);
            }

            return endpoint;
        }

        /// <summary>
        /// UDP 下发函数（保持原有接口，供发送端使用）
        /// </summary>
        public async Task<bool> SendUdpMessageAsync(IPEndPoint targetEP, byte[] data, CancellationToken cancellationToken = default)
        {
            if (targetEP == null)
            {
                _logger.LogWarning("UDP 下发失败：目标地址为空");
                return false;
            }

            if (data == null || data.Length == 0)
            {
                _logger.LogWarning("UDP 下发失败：发送数据为空，目标地址={RemoteEP}", targetEP);
                return false;
            }

            try
            {
                await _udpClient.SendAsync(data, data.Length, targetEP).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP 下发失败：目标地址={RemoteEP}", targetEP);
                return false;
            }
        }
    }
}