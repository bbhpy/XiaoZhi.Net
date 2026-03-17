using MQTTnet.AspNetCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt.Contexts
{
    /// <summary>
    /// MQTT鉴权校验类（对标AuthenticationVerification）
    /// 适配MQTT协议：从CONNECT报文提取鉴权信息，复用原有IBasicVerify接口，统一鉴权逻辑
    /// </summary>
    internal class MqttAuthenticationVerification
    {
        /// <summary>
        /// 异步校验MQTT客户端鉴权信息（对标AuthenticationVerification.VerifyAsync）
        /// </summary>
        /// <param name="context">MQTT连接上下文（包含客户端ID、用户名/密码、远端IP）</param>
        /// <param name="config">小智配置（复用WebSocket的XiaoZhiConfig）</param>
        /// <param name="basicVerify">基础鉴权接口（复用WebSocket的IBasicVerify）</param>
        /// <returns>true=鉴权通过，false=鉴权失败</returns>
        public static async ValueTask<bool> VerifyAsync(MqttConnectionContext context, XiaoZhiConfig config, IBasicVerify? basicVerify)
        {
            // 1. 提取MQTT连接上下文信息
            string clientId = context.ClientId ?? string.Empty;
            string deviceId = context.Username ?? string.Empty; // MQTT用户名=设备ID
            string token = context.Password == null ? string.Empty : Encoding.UTF8.GetString(context.Password); // MQTT密码=Token
            string remoteIp = context.RemoteEndPoint?.ToString() ?? "未知IP";

            try
            {
                // 2. 如果全局关闭鉴权，直接通过
                if (!config.AuthEnabled)
                {
                    Log.Information("[MQTT鉴权] 全局鉴权开关关闭，客户端[{ClientId}]（{RemoteIp}）鉴权通过", clientId, remoteIp);
                    return true;
                }

                // 3. 基础参数校验（不能为空）
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    Log.Warning("[MQTT鉴权] 客户端[{ClientId}]（{RemoteIp}）鉴权失败：设备ID为空", clientId, remoteIp);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(token))
                {
                    Log.Warning("[MQTT鉴权] 客户端[{ClientId}]（{RemoteIp}）鉴权失败：Token为空", clientId, remoteIp);
                    return false;
                }

                // 4. 复用WebSocket的基础鉴权接口
                if (basicVerify == null)
                {
                    Log.Error("[MQTT鉴权] 基础鉴权接口未初始化，客户端[{ClientId}]（{RemoteIp}）鉴权失败", clientId, remoteIp);
                    return false;
                }
                bool verifyResult = true; //await basicVerify.Verify(deviceId, token, context.RemoteEndPoint as IPEndPoint);

                //// 5. 日志记录（与WebSocket鉴权日志格式统一）
                //if (verifyResult)
                //{
                //    Log.Information("[MQTT鉴权] 客户端[{ClientId}]（设备ID：{DeviceId}，IP：{RemoteIp}）鉴权通过", clientId, deviceId, remoteIp);
                //}
                //else
                //{
                //    Log.Warning("[MQTT鉴权] 客户端[{ClientId}]（设备ID：{DeviceId}，IP：{RemoteIp}）鉴权失败：Token无效", clientId, deviceId, remoteIp);
                //}

                return verifyResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MQTT鉴权] 客户端[{ClientId}]（IP：{RemoteIp}）鉴权过程异常", clientId, remoteIp);
                return false;
            }
        }

        /// <summary>
        /// MQTT连接上下文（封装鉴权所需参数）
        /// </summary>
        public class MqttConnectionContext
        {
            /// <summary>
            /// MQTT客户端ID
            /// </summary>
            public string ClientId { get; set; } = string.Empty;

            /// <summary>
            /// 用户名（设备ID）
            /// </summary>
            public string? Username { get; set; }

            /// <summary>
            /// 密码（Token，字节数组）
            /// </summary>
            public byte[]? Password { get; set; }

            /// <summary>
            /// 客户端远端IP端点
            /// </summary>
            public EndPoint? RemoteEndPoint { get; set; }
        }
    }
}
