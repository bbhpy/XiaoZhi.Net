using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.AspNetCore;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts
{
    // 新增：MQTT后台服务（用于启动/停止MQTT服务）
    internal class MqttHostedService : BackgroundService
    {
        private readonly MqttService _mqttService;
        private readonly MqttServerConfig _mqttConfig;
        private readonly ILogger<MqttHostedService> _logger;

        public MqttHostedService(MqttService mqttService, XiaoZhiConfig xiaoZhiConfig, ILogger<MqttHostedService> logger)
        {
            _mqttService = mqttService ?? throw new ArgumentNullException(nameof(mqttService));
            _mqttConfig = xiaoZhiConfig.MqttConfig;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 1. 配置MQTT服务
                _mqttService.Configure(_mqttConfig, ValidateConnectionAsync);

                // 2. 启动MQTT服务
                await _mqttService.StartAsync(stoppingToken);
                _logger.LogInformation("MQTT服务已通过HostedService启动，监听端口：{Port}", _mqttConfig.Port);

                // ========== 核心修改：.NET 8 等待停止信号的正确写法 ==========
                // Timeout.Infinite 表示无限等待，直到 stoppingToken 被触发
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 正常停止信号，无需报错
                _logger.LogInformation("MQTT服务收到停止信号，开始优雅关闭...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT服务启动或运行失败");
                throw; // 抛出异常让HostedService感知启动失败
            }
            finally
            {
                // 确保无论是否异常，都停止MQTT服务
                if (_mqttService.IsStarted)
                {
                    await _mqttService.StopAsync(CancellationToken.None);
                    _logger.LogInformation("MQTT服务已成功停止");
                }
            }
        }
        /// <summary>
        /// MQTT客户端连接认证逻辑（全局用户名密码）
        /// 绑定时机：MqttService.Configure时注册
        /// </summary>
        /// <param name="args">连接验证参数（来自MQTTnet）</param>
        /// <returns>异步任务</returns>
        private async Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
        {
            // 异步包装（兼容同步逻辑）
            await Task.Yield();

            try
            {
                _logger.LogDebug("开始验证MQTT客户端连接：ClientId={ClientId}，用户名={Username}",
                    args.ClientId, args.UserName);

                // 1. 获取MQTT配置
                var mqttConfig = _mqttConfig;

                // 2. 用户名密码校验（空值处理）
                var providedUsername = args.UserName ?? string.Empty;
                var providedPassword = args.Password == null
                    ? string.Empty
                    : args.Password;

                if (providedUsername != mqttConfig.GlobalUsername || providedPassword != mqttConfig.GlobalPassword)
                {
                    // 认证失败：设置拒绝原因
                    args.ReasonString = "用户名或密码错误";
                    args.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
                    _logger.LogWarning("MQTT客户端认证失败：ClientId={ClientId}，错误原因={Reason}", args.ClientId, args.ReasonString);
                    return;
                }
                // 2. 新增：校验 ClientId 不能为空（必须传）
                if (string.IsNullOrEmpty(args.ClientId))
                {
                    args.ReasonString = "ClientId 不能为空（需唯一标识客户端）";
                    args.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
                    return;
                }

                // 3. 可选：禁止同一 ClientId 重复登录（踢掉旧连接/拒绝新连接）
                var existingClientIds = await GetAllClientIdsAsync();
                if (existingClientIds.Contains(args.ClientId))
                {
                    // 方案1：拒绝新连接（推荐）
                    args.ReasonString = $"ClientId {args.ClientId} 已登录，禁止重复连接";
                    args.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;

                    // 方案2：踢掉旧连接，允许新连接
                    //await DisconnectClientAsync(args.ClientId, "新连接替换旧连接");
                    return;
                }
                // 4. 认证通过，记录 ClientId 与用户名的关联
                args.ReasonCode = MqttConnectReasonCode.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT客户端认证过程出错：ClientId={ClientId}", args.ClientId);
                args.ReasonString = "服务器内部错误";
                args.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ServerUnavailable;
            }
        }

        /// <summary>
        /// 获取所有已连接的MQTT客户端ID
        /// 核心逻辑：转发到底层服务，无额外业务逻辑
        /// </summary>
        /// <returns>客户端ID列表</returns>
        public async Task<IEnumerable<string>> GetAllClientIdsAsync()
        {
            _logger.LogDebug("MqttProtocolEngine获取所有客户端ID");
            return await _mqttService.GetAllClientIdsAsync();
        }
    }
}
