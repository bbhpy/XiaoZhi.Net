using MQTTnet.Server.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Server.Protocol.Mqtt
{
    /// <summary>
    /// MQTT+UDP 会话存储类
    /// 继承DefaultMemoryStore，扩展SSRC映射能力
    /// </summary>
    internal class MqttUdpSessionStore : DefaultMemoryStore
    {
        // 多维度索引（线程安全）
        private readonly ConcurrentDictionary<string, string> _macToSessionId = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _deviceIdToSessionId = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _mqttClientIdToSessionId = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<uint, string> _ssrcToSessionId = new ConcurrentDictionary<uint, string>();

        // 单例（可选，保持和父类一致的使用习惯）
        public static new MqttUdpSessionStore Default => new MqttUdpSessionStore();

        // ========== 扩展：添加会话（自动维护索引） ==========
        /// <summary>
        /// 添加MqttUdpSession，并自动维护多维度索引（mac/deviceId/mqttClientId/ssrc）
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public bool AddSession(MqttUdpSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrEmpty(session.SessionId))
                throw new ArgumentException("SessionId 不能为空", nameof(session));

            // 1. 调用父类的Add方法添加会话到主存储
            bool added = base.Add(session.SessionId, session);
            if (!added)
                return false;

            // 2. 维护多维度索引（索引失败则回滚）
            try
            {
                if (!string.IsNullOrEmpty(session.MacAddress))
                    _macToSessionId.AddOrUpdate(session.MacAddress, session.SessionId, (_, __) => session.SessionId);

                if (!string.IsNullOrEmpty(session.DeviceId))
                    _deviceIdToSessionId.AddOrUpdate(session.DeviceId, session.SessionId, (_, __) => session.SessionId);

                if (!string.IsNullOrEmpty(session.MqttClientId))
                    _mqttClientIdToSessionId.AddOrUpdate(session.MqttClientId, session.SessionId, (_, __) => session.SessionId);

                _ssrcToSessionId.AddOrUpdate(session.Ssrc, session.SessionId, (_, __) => session.SessionId);
            }
            catch
            {
                // 索引维护失败，回滚会话添加
                base.Remove(session.SessionId);
                throw;
            }

            return true;
        }
        /// <summary>
        /// 获取MqttUdpSession
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        public MqttUdpSession? GetSession(string sessionId)
        {
            return base.Get<MqttUdpSession>(sessionId);
        }
        // ========== 扩展：按不同维度查询会话 ==========
        /// <summary>
        /// mac获取MqttUdpSession
        /// </summary>
        /// <param name="macAddress"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public MqttUdpSession? GetSessionByMac(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress))
                throw new ArgumentNullException(nameof(macAddress));

            return _macToSessionId.TryGetValue(macAddress, out string sessionId)
                ? base.Get<MqttUdpSession>(sessionId)
                : null;
        }
        /// <summary>
        /// 硬件deviceId获取MqttUdpSession
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public MqttUdpSession? GetSessionByDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentNullException(nameof(deviceId));

            return _deviceIdToSessionId.TryGetValue(deviceId, out string sessionId)
                ? base.Get<MqttUdpSession>(sessionId)
                : null;
        }
        /// <summary>
        /// mqttClientId获取MqttUdpSession
        /// </summary>
        /// <param name="mqttClientId"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public MqttUdpSession? GetSessionByMqttClientId(string mqttClientId)
        {
            if (string.IsNullOrEmpty(mqttClientId))
                throw new ArgumentNullException(nameof(mqttClientId));

            return _mqttClientIdToSessionId.TryGetValue(mqttClientId, out string sessionId)
                ? base.Get<MqttUdpSession>(sessionId)
                : null;
        }
        /// <summary>
        /// udp Ssrc获取MqttUdpSession
        /// </summary>
        /// <param name="ssrc"></param>
        /// <returns></returns>
        public MqttUdpSession? GetSessionBySsrc(uint ssrc)
        {
            return _ssrcToSessionId.TryGetValue(ssrc, out string sessionId)
                ? base.Get<MqttUdpSession>(sessionId)
                : null;
        }

        // ========== 扩展：删除会话（自动清理索引） ==========
        /// <summary>
        /// sessionId 删除会话，并清理相关索引
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public int RemoveSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentNullException(nameof(sessionId));

            // 1. 先获取会话（用于清理索引）
            MqttUdpSession session = null;
            try
            {
                session = base.Get<MqttUdpSession>(sessionId);
            }
            catch (KeyNotFoundException)
            {
                return 0;
            }

            // 2. 调用父类Remove方法删除主数据
            int removed = base.Remove(sessionId);
            if (removed == 0)
                return 0;

            // 3. 清理多维度索引
            if (session != null)
            {
                if (!string.IsNullOrEmpty(session.MacAddress))
                    _macToSessionId.TryRemove(session.MacAddress, out _);

                if (!string.IsNullOrEmpty(session.DeviceId))
                    _deviceIdToSessionId.TryRemove(session.DeviceId, out _);

                if (!string.IsNullOrEmpty(session.MqttClientId))
                    _mqttClientIdToSessionId.TryRemove(session.MqttClientId, out _);

                _ssrcToSessionId.TryRemove(session.Ssrc, out _);
            }

            return removed;
        }
        /// <summary>
        /// 更新会话属性（核心：覆盖更新，线程安全）
        /// </summary>
        /// <param name="session">待更新的会话实例</param>
        /// <returns>是否更新成功</returns>
        /// <exception cref="ArgumentNullException">session或ClientId为空时抛出</exception>
        public bool UpdateSession(MqttUdpSession session)
        {
            // 参数校验
            if (session == null)
                throw new ArgumentNullException(nameof(session), "待更新的会话实例不能为空");
            if (string.IsNullOrEmpty(session.SessionId))
                throw new ArgumentNullException(nameof(session.SessionId), "会话的ClientId不能为空");
            try
            {
                base.Update(session.SessionId, session);
                return true;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "更新会话失败：ClientId={ClientId}", session.MqttClientId);
                return false;
            }
        }
        // ========== 重写Clear：清空主数据+索引 ==========
        /// <summary>
        /// 全部清空会话数据和索引
        /// </summary>
        public override void Clear()
        {
            // 先清空父类的主数据
            base.Clear();
            // 再清空子类的索引
            _macToSessionId.Clear();
            _deviceIdToSessionId.Clear();
            _mqttClientIdToSessionId.Clear();
            _ssrcToSessionId.Clear();
        }
    }
}
