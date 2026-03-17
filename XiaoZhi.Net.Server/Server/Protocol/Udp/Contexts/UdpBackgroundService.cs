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
            ILogger<UdpBackgroundService> logger)
        {
            _mqttUdpSessionStore = sessionManager;
            _messageDispatch = messageDispatch;
            _config = config;
            _logger = logger;

            _udpClient = new UdpClient(_config.UdpConfig.Port);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UDP后台服务已启动，监听端口：{Port}", _config.UdpConfig.Port);

            // 注册取消令牌的回调，停止时关闭UDP客户端
            stoppingToken.Register(() =>
            {
                _logger.LogInformation("UDP后台服务正在停止...");
                _udpClient.Close();
                _udpClient.Dispose();
                _logger.LogInformation("UDP后台服务已停止");
            });

            try
            {
                // 循环接收UDP包，直到收到停止信号
                while (!stoppingToken.IsCancellationRequested)
                {
                    UdpReceiveResult receiveResult;
                    byte[] data;
                    IPEndPoint clientEP;
                    try
                    {
                        // 异步接收UDP包，支持取消
                        receiveResult = await _udpClient.ReceiveAsync(stoppingToken);
                        // 空包过滤
                        if (receiveResult.Buffer == null || receiveResult.Buffer.Length == 0)
                        {
                            _logger.LogWarning("收到空的UDP数据包，远端地址：{RemoteEndPoint}", receiveResult.RemoteEndPoint);
                            continue;
                        }
                        data = receiveResult.Buffer;
                        clientEP = receiveResult.RemoteEndPoint;

                        if (!UdpAudioPacket.TryParse(data, out var packet))
                        {
                            string bufferHex = BitConverter.ToString(data).Replace("-", ""); // 转十六进制（无分隔符）
                            string bufferText = Encoding.UTF8.GetString(data); // 同时转UTF8文本（兼容文本格式心跳包）
                            _logger.LogWarning(
                                "UDP数据包格式错误，客户端：{ClientEP}，数据包长度：{Length}字节，十六进制内容：{BufferHex}，UTF8文本：{BufferText}",
                                clientEP, data.Length, bufferHex, bufferText
                            );
                            //_logger.Warning("UDP数据包格式错误，客户端：{ClientEP}", clientEP);
                            continue;
                        }
                        var udpSession = _mqttUdpSessionStore.GetSessionBySsrc(packet.Ssrc);
                        if (udpSession != null)
                        {    
                            //await udpSession!.OnUdpMessageReceivedAsync(data);
                            udpSession.UpdateUdpRemoteEndPoint(clientEP);
                            udpSession.RefreshLastActivityTime();
                            byte[] bytes= AesKeyGenerator.DecryptUdpAudioPayload(packet.Payload,udpSession.UdpAesNonce,packet.PayloadLength,packet.Timestamp,packet.Sequence,udpSession.UdpAesKey);
                            //_logger.LogInformation("UDP音频包，客户端：{ClientEP}消息十六进制：{json}", clientEP, BitConverter.ToString(bytes).Replace("-", ""));
                            if (udpSession.XiaoZhiSession != null)
                            {
                                await udpSession.XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(bytes);
                            }
                            // 4. 分发UDP消息（交给业务层处理）
                            //await _messageDispatch.d(udpSession, receiveResult.Buffer, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常停止，无需记录错误
                        _logger.LogInformation("UDP接收操作已取消");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        // UDP客户端已释放，退出循环
                        _logger.LogInformation("UDP客户端已释放，停止接收");
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 非预期异常，记录并继续接收（避免服务崩溃）
                        _logger.LogError(ex, "UDP接收数据时发生异常");
                        // 短暂延迟，避免异常循环占用CPU
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "UDP后台服务主循环发生致命异常");
                // 可选：触发服务重启/告警
                throw;
            }
            finally
            {
                // 最终清理
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
            }

            _logger.LogInformation("UDP后台服务执行完成");
        }
        // 新增：线程安全的锁，避免并发发送/接收冲突
        private readonly object _udpSendLock = new object();
        /// <summary>
        /// 通用UDP下发函数（直接指定目标IP/Port）
        /// </summary>
        /// <param name="targetEP">目标客户端的IPEndPoint</param>
        /// <param name="data">二进制数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否发送成功</returns>
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
                lock (_udpSendLock)
                {
                    _udpClient.SendAsync(data, data.Length, targetEP);
                }
                //_logger.LogInformation("UDP下发成功：目标地址={RemoteEP}，数据长度={Length}字节",
                //    targetEP, data.Length);
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
