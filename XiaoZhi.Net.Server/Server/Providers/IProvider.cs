using System;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 模型提供者接口
    /// </summary>
    /// <typeparam name="TSettings"></typeparam>
    internal interface IProvider<TSettings> : IDisposable where TSettings : class
    {
        /// <summary>
        /// 模型提供者类型
        /// </summary>
        string ProviderType { get; }
        /// <summary>
        ///  模型名称
        /// </summary>
        string ModelName { get; }
        /// <summary>
        /// 是否为 sherpa 模型
        /// </summary>
        public bool IsSherpaModel { get; }
        /// <summary>
        ///  构建模型
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        bool Build(TSettings settings);
        /// <summary>
        ///  注册设备
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sessionId"></param>
        void RegisterDevice(string deviceId, string sessionId);
        /// <summary>
        ///  注销设备
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="sessionId"></param>
        void UnregisterDevice(string deviceId, string sessionId);
    }
}
