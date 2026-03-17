using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Services;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server
{
    /// <summary>
    /// 服务器构建器类，用于配置和构建服务器实例
    /// </summary>
    internal class ServerBuilder : IServerBuilder
    {
        /// <summary>
        /// 懒加载的单例服务器构建器实例
        /// </summary>
        private static readonly Lazy<IServerBuilder> lazyInstance = new Lazy<IServerBuilder>(() => new ServerBuilder());

        /// <summary>
        /// 私有构造函数，创建默认的主机构建器
        /// </summary>
        private ServerBuilder()
        {
            this.HostBuilder = Host.CreateDefaultBuilder();
        }

        /// <summary>
        /// 使用指定主机构建器初始化服务器构建器实例
        /// </summary>
        /// <param name="hostBuilder">主机构建器实例</param>
        internal ServerBuilder(IHostBuilder hostBuilder)
        {
            this.HostBuilder = hostBuilder;
        }

        /// <summary>
        /// 创建服务器构建器实例（单例模式）
        /// </summary>
        /// <returns>服务器构建器实例</returns>
        public static IServerBuilder CreateServerBuilder() => lazyInstance.Value;

        /// <summary>
        /// 创建服务器构建器实例（使用指定的主机构建器）
        /// </summary>
        /// <param name="hostBuilder">主机构建器实例</param>
        /// <returns>服务器构建器实例</returns>
        public static IServerBuilder CreateServerBuilder(IHostBuilder hostBuilder) => new ServerBuilder(hostBuilder);

        /// <summary>
        /// 获取或设置主机构建器
        /// </summary>
        public IHostBuilder HostBuilder { get; private set; }

        /// <summary>
        /// 使用默认连接存储初始化服务器配置
        /// </summary>
        /// <param name="config">小智配置对象</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder Initialize(XiaoZhiConfig config)
        {
            return this.Initialize(config, DefaultMemoryStore.Default);
        }

        /// <summary>
        /// 初始化服务器配置，注册必要的服务和组件
        /// </summary>
        /// <param name="config">小智配置对象</param>
        /// <param name="connectionStore">连接存储实例</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder Initialize(XiaoZhiConfig config, IStore connectionStore)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config), Lang.ServerBuilder_Initialize_ConfigNull);
            }
            this.HostBuilder = this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton(config);

                services.AddSingleton(connectionStore);

                services.AddKernel();
            })
            .RegisterLogger(config)
            .RegisterResources()
            .RegisterProviders(config)
            .RegisterHandlers()
            .RegisterObjectPools()
            .RegisterProtocol(config);

            // 根据编译环境设置运行时环境
#if DEBUG
        this.HostBuilder.UseEnvironment("Development");
#else
            this.HostBuilder.UseEnvironment("Production");
#endif

            return this;
        }

        /// <summary>
        /// 添加插件到服务器
        /// </summary>
        /// <typeparam name="TPlugin">插件类型</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder WithPlugin<TPlugin>(string pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentNullException(nameof(pluginName), Lang.ServerBuilder_WithPlugin_PluginNameNull);
            }
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton(sp => KernelPluginFactory.CreateFromType<TPlugin>(pluginName, sp));
            });
            return this;
        }

        /// <summary>
        /// 添加带有自定义函数的插件到服务器
        /// </summary>
        /// <typeparam name="TPlugin">插件类型</typeparam>
        /// <param name="pluginName">插件名称</param>
        /// <param name="functions">插件函数集合</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder WithPlugin<TPlugin>(string pluginName, IEnumerable<IFunction> functions)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentNullException(nameof(pluginName), Lang.ServerBuilder_WithPlugin_PluginNameNull);
            }
            if (functions == null || !functions.Any())
            {
                throw new ArgumentNullException(nameof(functions), Lang.ServerBuilder_WithPlugin_FunctionsNull);
            }

            IEnumerable<KernelFunction> kernelFunctions = functions.Select(f => KernelFunctionFactory.CreateFromMethod(f.Method, f.FunctionName, f.Description));
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton(sp => KernelPluginFactory.CreateFromFunctions(pluginName, kernelFunctions));
            });
            return this;
        }

        /// <summary>
        /// 注册验证服务
        /// </summary>
        /// <typeparam name="T">验证实现类型，必须实现IBasicVerify接口</typeparam>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder WithVerify<T>() where T : class, IBasicVerify
        {
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IBasicVerify, T>();
            });

            return this;
        }

        /// <summary>
        /// 配置管理API客户端
        /// </summary>
        /// <param name="manageApiUrl">管理API地址</param>
        /// <param name="secret">访问密钥</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder WithManageApi(string manageApiUrl, string secret)
        {
            this.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IFlurlClientCache>(_ => new FlurlClientCache()
                .Add("ManageApi", manageApiUrl, builder =>
                {
                    builder.WithOAuthBearerToken(secret);
                    builder.Settings.JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS);
                }));
                services.AddSingleton<ManageApiClient>();
            });
            return this;
        }

        /// <summary>
        /// 设置本地化文化信息
        /// </summary>
        /// <param name="culture">文化标识符，默认为"zh-CN"</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder WithCulture(string culture = "zh-CN")
        {
            if (!string.IsNullOrEmpty(culture))
            {
                CultureInfo cultureInfo = new CultureInfo(culture);
                Lang.Culture = cultureInfo;
            }
            else
            {
                Lang.Culture = CultureInfo.CurrentCulture;
            }

            return this;
        }

        /// <summary>
        /// 设置本地化文化信息
        /// </summary>
        /// <param name="culture">文化信息对象</param>
        /// <returns>当前服务器构建器实例</returns>
        public IServerBuilder WithCulture(CultureInfo culture)
        {
            if (culture is not null)
            {
                Lang.Culture = culture;
            }
            else
            {
                Lang.Culture = CultureInfo.CurrentCulture;
            }

            return this;
        }

        /// <summary>
        /// 构建并返回服务器主机实例
        /// </summary>
        /// <returns>构建完成的主机实例</returns>
        public IHost Build()
        {
            IHost host = this.HostBuilder.Build();

            this.BuildComponents(host.Services);

            return host;
        }

        /// <summary>
        /// 构建服务器组件，包括资源管理和提供者管理
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        private void BuildComponents(IServiceProvider serviceProvider)
        {
            ResourceManager resourceManager = serviceProvider.GetRequiredService<ResourceManager>();
            ProviderManager providerManager = serviceProvider.GetRequiredService<ProviderManager>();
            bool loaded = resourceManager.BuildComponent(serviceProvider);
            if (!loaded)
            {
                Serilog.Log.CloseAndFlush();
                throw new ApplicationException(Lang.ServerBuilder_BuildComponents_ResourceLoadFailed);
            }
            bool builded = providerManager.BuildComponent(serviceProvider);
            if (!builded)
            {
                Serilog.Log.CloseAndFlush();
                throw new ApplicationException(Lang.ServerBuilder_BuildComponents_ProviderBuildFailed);
            }
        }
    }
}
