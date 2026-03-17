using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Resources.DeviceBinding;
using XiaoZhi.Net.Server.Resources.Musics;
using XiaoZhi.Net.Server.Resources.OnnxModels;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD;

namespace XiaoZhi.Net.Server.Management
{
    /// <summary>
    /// 资源管理器类，负责管理和初始化系统中的各种资源组件
    /// </summary>
    internal class ResourceManager
    {
        private readonly XiaoZhiConfig _config;

        /// <summary>
        /// 初始化ResourceManager实例
        /// </summary>
        /// <param name="config">配置对象</param>
        public ResourceManager(XiaoZhiConfig config)
        {
            this._config = config;
        }

        /// <summary>
        /// 注册服务到主机构建器中
        /// </summary>
        /// <param name="builder">主机构建器</param>
        /// <returns>注册了服务的主机构建器</returns>
        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IDeviceBinding, DefaultDeviceBinding>();
                services.AddSingleton<IMusics, MusicProvider>();
                services.AddSingleton<IVadOnnxModel, SileroOnnx>();

                services.AddSingleton<ResourceManager>();
            });
        }

        /// <summary>
        /// 构建并加载所有组件
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        /// <returns>如果所有组件都成功加载则返回true，否则返回false</returns>
        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            #region DeviceBinding
            // 加载设备绑定配置
            IDeviceBinding deviceBinding = serviceProvider.GetRequiredService<IDeviceBinding>();
            if (!deviceBinding.Load(this._config.DeviceBindSetting))
            {
                return false;
            }
            #endregion

            #region Musics
            // 加载音乐提供者配置
            IMusics musics = serviceProvider.GetRequiredService<IMusics>();
            if (!musics.Load(this._config.MusicProviderSetting))
            {
                return false;
            }
            #endregion

            #region Onnx models
            // 加载VAD模型配置
            IVadOnnxModel vadOnnxModel = serviceProvider.GetRequiredService<IVadOnnxModel>();
            if (!vadOnnxModel.Load(this.GetSelectedSetting("VAD", this._config)))
            {
                return false;
            }
            #endregion

            return true;
        }

        /// <summary>
        /// 获取选中模型类型的设置
        /// </summary>
        /// <param name="selectedModelType">选中的模型类型</param>
        /// <param name="config">配置对象</param>
        /// <returns>模型设置对象</returns>
        private ModelSetting GetSelectedSetting(string selectedModelType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedModelType];
            Dictionary<string, string> setting = config.ConfiguredSettings[selectedModelType][selectedModel];

            ModelSetting modelSetting = new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };

            return modelSetting;
        }

        /// <summary>
        /// 释放所有资源
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        public void Dispose(IServiceProvider serviceProvider)
        {
            IList<IDisposable> resources = new List<IDisposable>
        {
            serviceProvider.GetRequiredService<IDeviceBinding>(),
            serviceProvider.GetRequiredService<IMusics>(),
            serviceProvider.GetRequiredService<IVadOnnxModel>()
        };

            foreach (IDisposable resource in resources)
            {
                resource.Dispose();
            }
        }
    }
}
