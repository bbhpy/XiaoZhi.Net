using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts
{
    /// <summary>
    /// MQTT底层服务封装类
    /// 作用：适配MQTTnet 5.1.0.1559版本原生API，处理纯技术层逻辑
    /// 职责边界：仅负责MQTT协议交互，无业务逻辑（认证、存储等交给上层引擎）
    /// </summary>
    internal class MqttService : IDisposable
    {
        /// <summary>
        /// MQTT服务端实例（MQTTnet 5.1.0.1559核心对象，无IMqttServer接口）
        /// </summary>
        private MqttServer _mqttServer;
        /// <summary>
        /// MQTT服务端实例（MQTTnet 5.1.0.1559核心对象，无IMqttServer接口）
        /// </summary>
        public MqttServer MqttServer => _mqttServer;
        /// <summary>
        /// MQTT服务端配置选项（由Builder构建，适配5.1.0.1559版本）
        /// </summary>
        private MqttServerOptions _serverOptions;

        /// <summary>
        /// Serilog日志实例（复用原有日志体系）
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// MQTT服务是否已启动
        /// 取值来源：MqttServer.IsStarted属性（5.1.0.1559版本原生属性）
        /// </summary>
        public bool IsStarted => _mqttServer?.IsStarted ?? false;
        // 新增：在MqttService中添加ServiceProvider字段（用于创建MqttSession）
        private readonly IServiceProvider _serviceProvider;

        private readonly XiaoZhiConfig _xiaoZhiConfig;

        private MqttUdpSessionStore _connectionStore;
        /// <summary>
        /// 构造函数（依赖注入）
        /// </summary>
        /// <param name="logger">Serilog日志实例（必填，用于记录MQTT服务日志）</param>
        public MqttService(ILogger logger, MqttUdpSessionStore store,XiaoZhiConfig xiaoZhiConfig, IServiceProvider serviceProvider)
        {
            _connectionStore = store ?? throw new ArgumentNullException(nameof(store), "store实例化为空");
            _xiaoZhiConfig = xiaoZhiConfig;
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider), "ServiceProvider实例化为空");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Serilog日志实例不能为空");
            _logger.Information("MqttService初始化：创建基础日志实例");
        }

        /// <summary>
        /// 配置MQTT服务端选项
        /// 调用时机：MQTT引擎初始化时（IMqttProtocolEngine.Initialize）
        /// </summary>
        /// <param name="mqttConfig">业务层配置（来自XiaoZhiConfig.MqttConfig）</param>
        /// <param name="connectionValidator">连接验证委托（业务层认证逻辑）</param>
        /// <exception cref="ArgumentNullException">mqttConfig或connectionValidator为空时抛出</exception>
        public void Configure(MqttServerConfig mqttConfig, Func<ValidatingConnectionEventArgs, Task> connectionValidator)
        {
            if (mqttConfig == null)
            {
                throw new ArgumentNullException(nameof(mqttConfig), "MQTT配置不能为空");
            }
            if (connectionValidator == null)
            {
                throw new ArgumentNullException(nameof(connectionValidator), "连接验证委托不能为空");
            }

            _logger.Information("开始配置MQTT服务端选项，监听端口：{Port}，TLS启用状态：{UseTls}",
                mqttConfig.Port, mqttConfig.UseTls);

            // 1. 创建MQTT服务端选项构建器（适配5.1.0.1559版本）
            var optionsBuilder = new MqttServerOptionsBuilder();

            // 2. 基础TCP配置
            optionsBuilder.WithDefaultEndpoint(); // 启用默认TCP端点（必填）
            optionsBuilder.WithDefaultEndpointPort(mqttConfig.Port); // 设置监听端口
            optionsBuilder.WithConnectionBacklog(mqttConfig.ConnectionBacklog); // 设置连接队列大小

            // 3. 持久会话配置（适配5.1.0.1559版本的WithPersistentSessions方法）
            optionsBuilder.WithPersistentSessions(mqttConfig.EnablePersistentSessions);

            // 4. 消息队列配置
            optionsBuilder.WithMaxPendingMessagesPerClient(mqttConfig.MaxPendingMessagesPerClient); // 每个客户端最大待处理消息数
            optionsBuilder.WithPendingMessagesOverflowStrategy(MQTTnet.Server.MqttPendingMessagesOverflowStrategy.DropNewMessage); // 溢出策略

            // 5. 心跳配置（适配5.1.0.1559版本的KeepAliveOptions）
            optionsBuilder.WithKeepAlive(); // 启用心跳机制（必填）
            var keepAliveOptions = new MqttServerKeepAliveOptions
            {
                MonitorInterval = TimeSpan.FromSeconds(mqttConfig.KeepAliveMonitorInterval), // 心跳监控间隔
                DisconnectClientWhenReadingPayload = mqttConfig.DisconnectClientWhenReadingPayload // 读取负载时是否断开超时连接
            };

            // 6. TLS加密配置（可选）
            if (mqttConfig.UseTls && !string.IsNullOrEmpty(mqttConfig.TlsCertPath))
            {
                _logger.Information("启用TLS加密，证书路径：{CertPath}", mqttConfig.TlsCertPath);
                optionsBuilder.WithEncryptedEndpoint(); // 启用加密端点
                optionsBuilder.WithEncryptedEndpointPort(8883); // TLS默认端口：8883
                optionsBuilder.WithEncryptionCertificate(new X509Certificate2(mqttConfig.TlsCertPath, mqttConfig.TlsCertPassword)); // 加载证书
                optionsBuilder.WithEncryptionSslProtocol(SslProtocols.Tls12); // 指定TLS版本（推荐Tls12）
                optionsBuilder.WithRemoteCertificateValidationCallback((sender, certificate, chain, sslPolicyErrors) =>
                {
                    // 证书验证逻辑（生产环境需严格校验）
                    if (sslPolicyErrors == SslPolicyErrors.None) return true;
                    _logger.Warning("TLS证书验证失败：{Errors}", sslPolicyErrors);
                    return false;
                });
            }

            // 7. 构建最终配置选项
            _serverOptions = optionsBuilder.Build();

            // 8. 手动设置心跳选项（5.1.0.1559版本需手动赋值）
            _serverOptions.KeepAliveOptions.MonitorInterval = TimeSpan.FromSeconds(mqttConfig.KeepAliveMonitorInterval);
            _serverOptions.KeepAliveOptions.DisconnectClientWhenReadingPayload = mqttConfig.DisconnectClientWhenReadingPayload;

            // 9. 创建MQTT服务端实例（适配5.1.0.1559版本的MqttServerFactory）
            var serverFactory = new MqttServerFactory();
            _mqttServer = serverFactory.CreateMqttServer(_serverOptions);

            // 10. 注册连接验证事件（业务层认证逻辑）
            _mqttServer.ValidatingConnectionAsync += connectionValidator;

            // 11. 注册核心事件（日志记录）
            RegisterCoreEvents();

            _logger.Information("MQTT服务端选项配置完成，持久会话状态：{PersistentSessions}",
                mqttConfig.EnablePersistentSessions);
        }

        /// <summary>
        /// 注册MQTT核心事件（日志记录、状态监控）
        /// 作用：记录客户端连接/断开、消息收发等关键行为
        /// </summary>
        private void RegisterCoreEvents()
        {
            // 客户端连接成功事件
            _mqttServer.ClientConnectedAsync += async args =>
            {
                // 订阅所有主题 以便在InterceptingPublishAsync事件中接收客户端发布的消息（核心修改）  不然客户端收不到消息 #是通配符，表示订阅所有主题
                //await _mqttServer.SubscribeAsync(args.ClientId, "#");
                // 从会话存储中获取对应会话（如果不存在，可创建新会话）

                    // 可选：自动创建新会话（根据业务需求决定）
                var session = ActivatorUtilities.CreateInstance<MqttUdpSession>(_serviceProvider);
                await session.SetMqttClientId (args.ClientId);
                _connectionStore.AddSession(session);
                _logger.Information("为ClientId：{ClientId}自动创建新的MqttUdpSession", args.ClientId);
                // 调用会话的连接成功事件
                await session.OnConnectedAsync(_mqttServer,args.ClientId, args.RemoteEndPoint);
                //await Task.CompletedTask;

            };

            // 客户端断开连接事件
            _mqttServer.ClientDisconnectedAsync += async args =>
            {
                // 从会话存储中获取对应会话
                var session = _connectionStore.GetSessionByMqttClientId(args.ClientId);
                if (session != null)
                {
                    // 调用会话的断开连接事件
                    await session.OnDisconnectedAsync(args.ReasonString ?? "未知原因");
                    // 可选：断开后移除会话（根据业务需求）
                    _connectionStore.RemoveSession(args.ClientId);
                }
                //await Task.CompletedTask;
                _logger.Information("MQTT客户端断开连接，ClientId：{ClientId}，原因：{Reason}",
                    args.ClientId, args.ReasonString);
            };

            // 消息发布拦截事件（记录收到的JSON格式消息）
            _mqttServer.InterceptingPublishAsync += async args =>
            {
                // ========== 核心修改：兼容版本的属性读取逻辑 ==========
                bool isServerMessage = false;
                // 遍历用户属性，查找服务端标记
                if (args.ApplicationMessage.UserProperties != null && args.ApplicationMessage.UserProperties.Any())
                {
                    foreach (var prop in args.ApplicationMessage.UserProperties)
                    {
                        // 对比属性名 + 解析字节数组为字符串
                        if (prop.Name == "IsServerMessage")
                        {
                            string propValue = System.Text.Encoding.UTF8.GetString(prop.ValueBuffer.ToArray());
                            if (propValue == "true")
                            {
                                isServerMessage = true;
                                break;
                            }
                        }
                    }
                }

                // 如果是服务端消息，直接跳过
                if (isServerMessage)
                {
                    _logger.Debug("忽略服务端自身发布的MQTT消息，主题：{Topic}", args.ApplicationMessage.Topic);
                    return;
                }

                string payloadStr = string.Empty;
                try
                {
                    if (args.ApplicationMessage == null)
                    {
                        _logger.Warning("拦截到空的MQTT消息，ClientId：{ClientId}", args.ClientId);
                        return;
                    }

                    var topic = args.ApplicationMessage.Topic;
                    var payload = args.ApplicationMessage.Payload;

                    // 1. 将 ReadOnlySequence<byte> 转成字节数组（适配5.1.0.1559版本）
                    byte[] payloadBytes = args.ApplicationMessage.Payload.ToArray();
                    // 2. 用UTF8编码转字符串（JSON默认UTF8）
                    payloadStr = System.Text.Encoding.UTF8.GetString(payloadBytes);
                    //_logger.Debug("收到MQTT消息，ClientId：{ClientId}，Topic：{Topic}，Payload长度：{Length},内容：{neir}",
                    //    args.ClientId, topic, payload.Length, payloadStr);

                    // 调用会话的消息接收事件
                    await MqttMessageDispatch(args.ClientId, topic, payload.ToArray());
                }
                catch (JsonSerializationException ex)
                {
                    payloadStr = $"JSON解析失败：{ex.Message}";
                    _logger.Warning(ex, "MQTT消息JSON解析失败，ClientId：{ClientId}，Topic：{Topic}",
                        args.ClientId, args.ApplicationMessage.Topic);
                }
                catch (Exception ex)
                {
                    payloadStr = $"解析消息失败：{ex.Message}";
                    _logger.Warning(ex, "MQTT消息负载解析失败，ClientId：{ClientId}，Topic：{Topic}",
                        args.ClientId, args.ApplicationMessage.Topic);
                }
                await Task.CompletedTask;
            };

            // 服务启动成功事件
            _mqttServer.StartedAsync += async args =>
            {
                await Task.CompletedTask;
                _logger.Information("MQTT服务端启动成功，监听端口：{Port}", _serverOptions.DefaultEndpointOptions.Port);
            };

            // 服务停止事件
            _mqttServer.StoppedAsync += async args =>
            {
                await Task.CompletedTask;
                _logger.Information("MQTT服务端已停止");
            };
        }

        public async Task MqttMessageDispatch(string clientId, string topic, byte[] payload)
        {
            try
            {
                // 从会话存储中获取对应会话
                var session = _connectionStore.GetSessionByMqttClientId(clientId);
                string payloadStr = System.Text.Encoding.UTF8.GetString(payload);
                // 2. 解析JSON判断是否为Hello请求（核心新增）
                if (session != null && !string.IsNullOrEmpty(payloadStr))
                {
                    dynamic requestData = JsonConvert.DeserializeObject<dynamic>(payloadStr);
                    JsonNode? jsonObject = JsonNode.Parse(payloadStr);
                    string? type = jsonObject?["type"]?.GetValue<string>()?.ToLower();
                    if (jsonObject is JsonObject jsonObj && !string.IsNullOrEmpty(type) && type == "hello")
                    {
                        _logger.Information("客户端{SessionId}发送来Hello消息，Json为：{json}", session.SessionId, payloadStr);
                        await session.XiaoZhiSession.HandlerPipeline.HandleHelloMessage(jsonObj);
                    }
                    else
                    {
                        _logger.Information("客户端{SessionId}发送来文本消息：{json}", session.SessionId, payloadStr);
                        session.XiaoZhiSession.HandlerPipeline.HandleTextMessage(payloadStr);
                    }

                    //if (requestData?.type == "hello")
                    //{
                    //    var replyData = new
                    //    {
                    //        type = "hello",
                    //        transport = "udp",
                    //        session_id = session.SessionId,
                    //        audio_params = new
                    //        {
                    //            format = "opus",
                    //            sample_rate = 16000,
                    //            channels = 1,
                    //            frame_duration = 60
                    //        },
                    //        udp = new
                    //        {
                    //            server = "192.168.1.37",
                    //            port = _xiaoZhiConfig.UdpConfig.Port,
                    //            key = session.UdpAesKey,
                    //            nonce = session.UdpAesNonce,
                    //            assigned_ssrc = session.Ssrc
                    //        }
                    //    };
                    //     topic = $"device/{session.SessionId}/hello";

                    //    _logger.Information("MQTT消息发布，Topic：{Topic}", topic);
                    //    string replyJson = JsonConvert.SerializeObject(replyData, Formatting.None);
                    //    await session.SendAsync(replyJson,topic);
                    //}
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT消息发布失败，Topic：{Topic}", topic);
                throw new InvalidOperationException("MQTT消息发布失败", ex);
            }
        }

        /// <summary>
        /// 启动MQTT服务端
        /// 适配：5.1.0.1559版本的MqttServer.StartAsync()无参数
        /// </summary>
        /// <param name="cancellationToken">取消令牌（用于终止启动）</param>
        /// <returns>异步任务</returns>
        /// <exception cref="InvalidOperationException">未配置或已启动时抛出</exception>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            // 状态校验
            if (_serverOptions == null)
            {
                throw new InvalidOperationException("MQTT服务端未配置，请先调用Configure方法");
            }
            if (IsStarted)
            {
                _logger.Warning("MQTT服务端已启动，无需重复启动");
                return;
            }

            try
            {
                _logger.Information("启动MQTT服务端...");
                // 适配5.1.0.1559版本：StartAsync无参数，需用CancellationToken监听取消
                using (cancellationToken.Register(() => _logger.Warning("MQTT服务端启动被取消")))
                {
                    await _mqttServer.StartAsync();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("MQTT服务端启动被取消");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT服务端启动失败");
                throw new InvalidOperationException("MQTT服务端启动失败", ex);
            }
        }

        /// <summary>
        /// 停止MQTT服务端
        /// 适配：5.1.0.1559版本的MqttServer.StopAsync需传入StopOptions
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!IsStarted)
            {
                _logger.Warning("MQTT服务端未启动，无需停止");
                return;
            }

            try
            {
                _logger.Information("停止MQTT服务端...");
                // 构建停止选项（适配5.1.0.1559版本）
                var stopOptions = new MqttServerStopOptionsBuilder().Build();
                using (cancellationToken.Register(() => _logger.Warning("MQTT服务端停止被取消")))
                {
                    await _mqttServer.StopAsync(stopOptions);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("MQTT服务端停止被取消");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT服务端停止失败");
                throw new InvalidOperationException("MQTT服务端停止失败", ex);
            }
        }
       
        /// <summary>
        /// 获取所有已连接的客户端ID
        /// 适配：5.1.0.1559版本的GetClientsAsync返回IList<MqttClientStatus>
        /// </summary>
        /// <returns>客户端ID列表</returns>
        public async Task<IEnumerable<string>> GetAllClientIdsAsync()
        {
            if (!IsStarted)
            {
                _logger.Warning("MQTT服务端未启动，无法获取客户端列表");
                return Enumerable.Empty<string>();
            }

            try
            {
                var clients = await _mqttServer.GetClientsAsync();
                var clientIds = clients.Select(c => c.Id).ToList();
                _logger.Information("获取MQTT客户端列表成功，数量：{Count}", clientIds.Count);
                return clientIds;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取MQTT客户端列表失败");
                throw new InvalidOperationException("获取MQTT客户端列表失败", ex);
            }
        }
        /// <summary>
        /// 发布MQTT消息
        /// 适配：5.1.0.1559版本使用InjectApplicationMessage替代PublishAsync
        /// </summary>
        /// <param name="topic">消息主题</param>
        /// <param name="payload">消息负载</param>
        /// <param name="qosLevel">服务质量等级</param>
        /// <param name="retain">是否保留消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        /// <exception cref="InvalidOperationException">服务未启动时抛出</exception>
        public async Task PublishMessageAsync(
            string topic,
            byte[] payload,
            MQTTnet.Protocol.MqttQualityOfServiceLevel mqttNetQosLevel,
            bool retain,
            CancellationToken cancellationToken = default)
        {
            if (!IsStarted)
            {
                throw new InvalidOperationException("MQTT服务端未启动，无法发布消息");
            }
            byte[] serverFlagBytes = System.Text.Encoding.UTF8.GetBytes("true");
            // 1. 先构建 MqttApplicationMessage（用 Builder 避免类型转换问题）
            var appMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload ?? Array.Empty<byte>())
                .WithQualityOfServiceLevel(mqttNetQosLevel)
                .WithRetainFlag(retain)
                .WithUserProperty("IsServerMessage", serverFlagBytes)
                .Build();
            // 构建注入消息（适配5.1.0.1559版本的InjectApplicationMessage方法）
            var injectedMessage = new InjectedMqttApplicationMessage(appMessage)
            {
                SenderClientId = "XiaoZhi_Server"
            };

            try
            {
                _logger.Debug("发布MQTT消息，主题：{Topic}，负载长度：{Length}字节",
                    topic, injectedMessage.ApplicationMessage.Payload.Length);
                await _mqttServer.InjectApplicationMessage(injectedMessage, cancellationToken);
                _logger.Information("MQTT消息发布成功，主题：{Topic}", topic);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT消息发布失败，主题：{Topic}", topic);
                throw new InvalidOperationException($"发布MQTT消息失败：{topic}", ex);
            }
        }
        /// <summary>
        /// 释放资源（实现IDisposable接口）
        /// 作用：释放MqttServer实例，避免内存泄漏
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            _logger.Information("MqttService资源已释放");
        }

        /// <summary>
        /// 释放资源（析构函数调用）
        /// </summary>
        /// <param name="disposing">是否手动释放（true=手动，false=GC自动）</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 释放托管资源：停止并销毁MqttServer
                if (_mqttServer != null && IsStarted)
                {
                    try
                    {
                        var stopOptions = new MqttServerStopOptionsBuilder().Build();
                        _mqttServer.StopAsync(stopOptions).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "释放MqttServer资源时停止失败");
                    }
                    _mqttServer.Dispose();
                }
            }

            // 释放非托管资源（无）
            _mqttServer = null;
            _serverOptions = null;
        }

        /// <summary>
        /// 析构函数（防止未手动Dispose时内存泄漏）
        /// </summary>
        ~MqttService()
        {
            Dispose(false);
        }
    }
    /// <summary>
    /// 生成随机字符串
    /// </summary>
    public static class RandomStringGenerator
    {
        // 定义包含大小写字母的字符池（无符号）
        private const string CharPool = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        // 线程安全的随机数生成器（避免多线程下重复）
        private static readonly Random _random = new Random();
        // 服务端维护全局自增SSRC（从1000开始，避开默认值）
        private static uint _globalSsrcCounter = 990011;
        private static readonly object _ssrcLock = new object();

        private const string Separator = "@@@";

        /// <summary>
        /// 生成指定长度的随机字符串（仅包含大小写字母，无符号）
        /// </summary>
        /// <param name="length">字符串长度（必须大于0）</param>
        /// <returns>随机字符串</returns>
        /// <exception cref="ArgumentOutOfRangeException">长度小于等于0时抛出</exception>
        public static string GenerateRandomString(int length)
        {
            // 校验长度合法性
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "字符串长度必须大于0");
            }

            // 使用StringBuilder高效拼接字符（避免字符串频繁拼接的性能损耗）
            StringBuilder sb = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                // 随机获取字符池中的一个字符
                int randomIndex = _random.Next(CharPool.Length);
                sb.Append(CharPool[randomIndex]);
            }

            return sb.ToString();
        }
        /// <summary>
        /// Ssrc    生成器（线程安全自增，保证唯一性）
        /// </summary>
        /// <returns></returns>
        public static uint GenerateUniqueSsrc()
        {
            lock (_ssrcLock)
            {
                return _globalSsrcCounter++; // 自增保证绝对唯一
            }
        }
        /// <summary>
        /// 将两个参数合并为指定格式的字符串（格式：参数1@@@参数2）
        /// </summary>
        /// <param name="param1">第一个参数（如：GID_test）</param>
        /// <param name="param2">第二个参数（如：00_15_5d_b3_0c_62@@@30ff1138-1a98-4de3-9e88-0d1a05dd562e）</param>
        /// <returns>合并后的完整字符串</returns>
        /// <exception cref="ArgumentNullException">参数为空时抛出</exception>
        public static string CombineTwoParams(string param1, string param2)
        {
            // 校验参数非空
            if (string.IsNullOrWhiteSpace(param1))
                throw new ArgumentNullException(nameof(param1), "第一个参数不能为空或空白");
            if (string.IsNullOrWhiteSpace(param2))
                throw new ArgumentNullException(nameof(param2), "第二个参数不能为空或空白");

            // 拼接成指定格式
            return $"{param1}{Separator}{param2}";
        }

        /// <summary>
        /// 从合并后的字符串中解析出两个原始参数
        /// </summary>
        /// <param name="combinedStr">合并后的字符串（如：GID_test@@@00_15_5d_b3_0c_62@@@30ff1138-1a98-4de3-9e88-0d1a05dd562e）</param>
        /// <param name="param1">解析出的第一个参数</param>
        /// <param name="param2">解析出的第二个参数</param>
        /// <returns>解析是否成功（true=成功，false=失败）</returns>
        public static string[] ParseCombinedString(string combinedStr)
        {
            string[] ret = [];

            // 校验输入字符串非空
            if (string.IsNullOrWhiteSpace(combinedStr))
                return ret;

            // 按分隔符分割，只分割一次（保证第二个参数中包含分隔符也能正确解析）
            string[] parts = combinedStr.Split(Separator);

            // 分割后必须恰好有2个部分才视为解析成功
            if (parts.Length < 3)
                return ret;

            // 赋值输出参数
            ret[0] = parts[1];
            ret[1] = parts[2];
            return ret;
        }
    }
}
