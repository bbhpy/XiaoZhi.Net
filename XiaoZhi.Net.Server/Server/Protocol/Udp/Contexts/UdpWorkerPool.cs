using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Server.Protocol.Mqtt;

namespace XiaoZhi.Net.Server.Server.Protocol.Udp.Contexts
{
    /// <summary>
    /// UDP Worker 池服务
    /// 职责：
    /// 1. 管理 N 个独立的 Channel，每个 Channel 对应一个 Worker 线程
    /// 2. 按 SSRC 哈希路由到固定的 Worker，保证同一终端的包串行处理
    /// 3. 在 Worker 中执行解密、序列号校验、端点更新等操作
    /// 4. 将处理后的音频数据送入 Handler Pipeline
    /// </summary>
    internal class UdpWorkerPool : BackgroundService
    {
        private readonly Channel<UdpWorkItem>[] _channels;
        private readonly int _workerCount;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<UdpWorkerPool> _logger;
        private readonly XiaoZhiConfig _config;

        /// <summary>
        /// 初始化 UDP Worker 池
        /// </summary>
        /// <param name="serviceProvider">服务提供者，用于获取瞬态服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="config">小智配置</param>
        public UdpWorkerPool(
            IServiceProvider serviceProvider,
            ILogger<UdpWorkerPool> logger,
            XiaoZhiConfig config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _config = config;

            // Worker 数量：优先使用配置，否则使用 CPU 核心数
            _workerCount = config.UdpConfig?.WorkerCount > 0
                ? config.UdpConfig.WorkerCount
                : Environment.ProcessorCount;

            // 队列容量：每个 Worker 独立的 Channel 容量
            int queueSize = config.UdpConfig?.QueueSize > 0
                ? config.UdpConfig.QueueSize
                : 1000;

            // 初始化 Channel 数组
            _channels = new Channel<UdpWorkItem>[_workerCount];
            for (int i = 0; i < _workerCount; i++)
            {
                var options = new BoundedChannelOptions(queueSize)
                {
                    FullMode = BoundedChannelFullMode.Wait  // 背压：队列满时等待
                };
                _channels[i] = Channel.CreateBounded<UdpWorkItem>(options);
            }

            _logger.LogInformation(
                "UDP Worker 池初始化完成：WorkerCount={WorkerCount}，每个队列容量={QueueSize}",
                _workerCount, queueSize);
        }

        /// <summary>
        /// 将 UDP 工作项入队，按 SSRC 哈希路由到固定 Worker
        /// </summary>
        /// <param name="workItem">UDP 工作项</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async ValueTask EnqueueAsync(UdpWorkItem workItem, CancellationToken cancellationToken = default)
        {
            // 按 SSRC 哈希路由，保证同一终端的包始终进入同一个 Worker
            int index = (int)(workItem.Ssrc % (uint)_workerCount);
            await _channels[index].Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 后台服务主循环：启动所有 Worker 线程
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UDP Worker 池服务已启动，Worker 数量：{WorkerCount}", _workerCount);

            // 为每个 Worker 启动独立的任务
            var tasks = new Task[_workerCount];
            for (int i = 0; i < _workerCount; i++)
            {
                var channel = _channels[i];
                var workerIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    await RunWorkerAsync(workerIndex, channel.Reader, stoppingToken);
                }, stoppingToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogInformation("UDP Worker 池服务已停止");
        }

        /// <summary>
        /// 单个 Worker 的执行逻辑
        /// </summary>
        /// <param name="workerIndex">Worker 索引</param>
        /// <param name="reader">Channel 读取器</param>
        /// <param name="stoppingToken">取消令牌</param>
        private async Task RunWorkerAsync(
            int workerIndex,
            ChannelReader<UdpWorkItem> reader,
            CancellationToken stoppingToken)
        {
            _logger.LogDebug("UDP Worker {WorkerIndex} 已启动", workerIndex);

            await foreach (var workItem in reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessWorkItemAsync(workItem, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("UDP Worker {WorkerIndex} 操作已取消", workerIndex);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "UDP Worker {WorkerIndex} 处理工作项时发生异常，SSRC={Ssrc}，远端={RemoteEP}",
                        workerIndex, workItem.Ssrc, workItem.RemoteEndPoint);
                }
            }

            _logger.LogDebug("UDP Worker {WorkerIndex} 已停止", workerIndex);
        }

        /// <summary>
        /// 处理单个 UDP 工作项
        /// </summary>
        /// <param name="workItem">UDP 工作项</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task ProcessWorkItemAsync(UdpWorkItem workItem, CancellationToken cancellationToken)
        {
            // 1. 格式校验：包长度至少 16 字节（头部）
            if (workItem.RawData == null || workItem.RawData.Length < 16)
            {
                _logger.LogWarning(
                    "UDP 数据包长度不足，SSRC={Ssrc}，远端={RemoteEP}，长度={Length}",
                    workItem.Ssrc, workItem.RemoteEndPoint, workItem.RawData?.Length ?? 0);
                return;
            }

            // 2. 校验 Type 字段（固定为 0x01）
            byte type = workItem.RawData[0];
            if (type != 0x01)
            {
                _logger.LogWarning(
                    "UDP 数据包 Type 字段错误，SSRC={Ssrc}，远端={RemoteEP}，Type={Type}",
                    workItem.Ssrc, workItem.RemoteEndPoint, type);
                return;
            }

            // 3. 创建服务范围，用于获取瞬态服务（确保会话隔离）
            using var scope = _serviceProvider.CreateScope();

            // 4. 获取会话存储和消息分发器
            var sessionStore = scope.ServiceProvider.GetRequiredService<MqttUdpSessionStore>();
            var messageDispatch = scope.ServiceProvider.GetRequiredService<UdpMessageDispatch>();

            // 5. 根据 SSRC 获取会话
            var udpSession = sessionStore.GetSessionBySsrc(workItem.Ssrc);
            if (udpSession == null)
            {
                _logger.LogWarning(
                    "未找到 SSRC={Ssrc} 对应的 UDP 会话，远端={RemoteEP}，数据包已丢弃",
                    workItem.Ssrc, workItem.RemoteEndPoint);
                return;
            }

            // 6. 检查会话是否有效（MQTT 连接状态）
            if (!udpSession.IsMqttConnected)
            {
                _logger.LogDebug(
                    "SSRC={Ssrc} 对应的 MQTT 会话已断开，数据包已丢弃，远端={RemoteEP}",
                    workItem.Ssrc, workItem.RemoteEndPoint);
                return;
            }

            // 7. 更新 UDP 端点（NAT 场景下 IP/端口可能变化）
            //    比较端点是否变化，变化时更新并记录日志
            bool endpointChanged = udpSession.UdpRemoteEndPoint == null ||
                                   !udpSession.UdpRemoteEndPoint.Equals(workItem.RemoteEndPoint);
            if (endpointChanged)
            {
                _logger.LogDebug(
                    "SSRC={Ssrc} UDP 端点更新：旧={OldEP} -> 新={NewEP}",
                    workItem.Ssrc, udpSession.UdpRemoteEndPoint, workItem.RemoteEndPoint);
                udpSession.UpdateUdpRemoteEndPoint(workItem.RemoteEndPoint);
            }

            // 8. 刷新最后活动时间
            udpSession.RefreshLastActivityTime();

            // 9. 调用消息分发器处理（解密 + 序列号校验 + 送入 Handler Pipeline）
            //    UdpMessageDispatch.DispatchAsync 内部会完成：
            //    - 解析数据包格式
            //    - 解密 Payload（AES-GCM）
            //    - 序列号校验（防重放、防乱序）
            //    - 调用 HandlerPipeline.HandleBinaryMessageAsync
            await messageDispatch.DispatchAsync(udpSession, workItem.RawData, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 获取当前 Worker 池的状态（用于监控）
        /// </summary>
        public (int WorkerCount, long[] QueueLengths) GetStatus()
        {
            var queueLengths = new long[_workerCount];
            for (int i = 0; i < _workerCount; i++)
            {
                queueLengths[i] = _channels[i].Reader.Count;
            }
            return (_workerCount, queueLengths);
        }
    }
}
