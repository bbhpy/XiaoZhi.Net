using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Handlers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Management
{
    /// <summary>
    /// 处理器管理器，负责管理和初始化各种消息处理器
    /// </summary>
    internal class HandlerManager
    {
#if DEBUG
    /// <summary>
    /// 调试模式下的通道容量
    /// </summary>
    private const int CHANNEL_CAPACITY = 100;
#else
        /// <summary>
        /// 发布模式下的通道容量
        /// </summary>
        private const int CHANNEL_CAPACITY = 200;
#endif

        /// <summary>
        /// 服务提供程序，用于获取处理器实例
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 日志记录器
        /// </summary>
        private readonly ILogger<HandlerManager> _logger;

        /// <summary>
        /// 初始化处理器管理器
        /// </summary>
        /// <param name="serviceProvider">服务提供程序</param>
        /// <param name="logger">日志记录器</param>
        public HandlerManager(IServiceProvider serviceProvider, ILogger<HandlerManager> logger)
        {
            this._serviceProvider = serviceProvider;
            this._logger = logger;
        }

        /// <summary>
        /// 注册服务到主机构建器
        /// </summary>
        /// <param name="builder">主机构建器</param>
        /// <returns>注册了服务的主机构建器</returns>
        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                // 所有业务处理器注册为Transient（每次解析新实例，保证会话隔离）
                services.AddTransient<HelloMessageHandler>();
                services.AddTransient<TextHandler>();
                services.AddTransient<AudioReceiveHandler>();
                services.AddTransient<Audio2TextHandler>();
                services.AddTransient<DialogueHandler>();
                services.AddTransient<Text2AudioHandler>();
                services.AddTransient<AudioProcessorHandler>();
                services.AddTransient<AudioSendHandler>();
                //单例：全局唯一，统筹所有会话的处理器初始化。
                services.AddSingleton<HandlerManager>();
            });
        }

        /// <summary>
        /// 初始化欢迎消息处理器
        /// </summary>
        /// <param name="session">会话对象</param>
        public void InitializeHelloMessageHandler(Session session)
        {
            var helloMessageHandler = this._serviceProvider.GetRequiredService<HelloMessageHandler>();
            this.InitializeSendOutter(session, helloMessageHandler);

            session.HandlerPipeline.InitHelloMessageHandler(helloMessageHandler);
        }

        /// <summary>
        /// 初始化私有配置和处理器管道
        /// </summary>
        /// <param name="session">会话对象</param>
        /// <returns>初始化是否成功</returns>
        public bool InitializePrivateConfig(Session session)
        {
            var textHandler = this._serviceProvider.GetRequiredService<TextHandler>();
            var audioReceiveHandler = this._serviceProvider.GetRequiredService<AudioReceiveHandler>();
            var audio2TextHandler = this._serviceProvider.GetRequiredService<Audio2TextHandler>();
            var dialogueHandler = this._serviceProvider.GetRequiredService<DialogueHandler>();
            var text2AudioHandler = this._serviceProvider.GetRequiredService<Text2AudioHandler>();
            var audioProcessorHandler = this._serviceProvider.GetRequiredService<AudioProcessorHandler>();
            var audioSendHandler = this._serviceProvider.GetRequiredService<AudioSendHandler>();

            // 创建处理器容器，将处理器名称映射到处理器实例
            IDictionary<string, IHandler> handlerContainer = new Dictionary<string, IHandler>
            {
                [textHandler.HandlerName] = textHandler,
                [audioReceiveHandler.HandlerName] = audioReceiveHandler,
                [audio2TextHandler.HandlerName] = audio2TextHandler,
                [dialogueHandler.HandlerName] = dialogueHandler,
                [text2AudioHandler.HandlerName] = text2AudioHandler,
                [audioProcessorHandler.HandlerName] = audioProcessorHandler,
                [audioSendHandler.HandlerName] = audioSendHandler
            };

            // 设置处理器之间的事件关联
            textHandler.OnManualStop += audioReceiveHandler.HandleManualStop;
            audioReceiveHandler.OnNoVoiceCloseConnect += dialogueHandler.NoVoiceCloseConnect;

            // 初始化处理器的发送外部接口
            this.InitializeSendOutter(session, audioReceiveHandler);
            this.InitializeSendOutter(session, audio2TextHandler);
            this.InitializeSendOutter(session, textHandler);
            this.InitializeSendOutter(session, dialogueHandler);
            this.InitializeSendOutter(session, text2AudioHandler);
            this.InitializeSendOutter(session, audioProcessorHandler);
            this.InitializeSendOutter(session, audioSendHandler);

            // 为处理器调度中止事件处理
            this.ScheduleOnAbort(textHandler);
            this.ScheduleOnAbort(audioReceiveHandler);
            this.ScheduleOnAbort(audio2TextHandler);
            this.ScheduleOnAbort(dialogueHandler);
            this.ScheduleOnAbort(text2AudioHandler);
            this.ScheduleOnAbort(audioProcessorHandler);
            this.ScheduleOnAbort(audioSendHandler);

            // 并行构建所有处理器的管道
            bool buildResults = handlerContainer.Values
               .AsParallel()
               .Select(h => h.Build(session.PrivateProvider))
               .All(result => result);

            if (!buildResults)
            {
                this._logger.LogError(Lang.HandlerManager_InitializePrivateConfig_BuildPipelineFailed, session.DeviceId);
                return false;
            }

            // 构建处理器工作流连接
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, audioReceiveHandler, audio2TextHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, textHandler, audio2TextHandler, dialogueHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, dialogueHandler, text2AudioHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, text2AudioHandler, audioProcessorHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, audioProcessorHandler, audioSendHandler);

            session.HandlerPipeline.InitHandlerPipeline(handlerContainer);

            return true;
        }

        /// <summary>
        /// 初始化处理器的发送外部接口
        /// </summary>
        /// <param name="session">会话对象</param>
        /// <param name="outHandler">输出处理器</param>
        private void InitializeSendOutter(Session session, IHandler outHandler)
        {
            outHandler.SendOutter = session.SendOutter;
        }

        /// <summary>
        /// 构建两个处理器之间的工作流连接
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="channelCapacity">通道容量</param>
        /// <param name="previous">前一个处理器</param>
        /// <param name="next">后一个处理器</param>
        private void BuildHandlersWorkflow<T>(int channelCapacity, IOutHandler<T> previous, IInHandler<T> next)
        {
            //// 有界通道配置：单读单写（高性能）、满队列时阻塞（避免丢数据）
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(channelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            Channel<Workflow<T>> channel = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous.NextWriter = channel.Writer;
            next.PreviousReader = channel.Reader;

            Task.Run(next.Handle);
            this._logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous.GetType().Name, next.GetType().Name);
        }

        /// <summary>
        /// 构建支持三种泛型类型的处理器工作流连接
        /// </summary>
        /// <typeparam name="T1">第一种数据类型</typeparam>
        /// <typeparam name="T2">第二种数据类型</typeparam>
        /// <typeparam name="T3">第三种数据类型</typeparam>
        /// <param name="channelCapacity">通道容量</param>
        /// <param name="previous">前一个处理器</param>
        /// <param name="next">后一个处理器</param>
        private void BuildHandlersWorkflow<T1, T2, T3>(int channelCapacity, IOutHandler<T1, T2, T3> previous, IInHandler<T1, T2, T3> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(channelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            Channel<Workflow<T1>> channel = Channel.CreateBounded<Workflow<T1>>(boundedChannelOptions);
            previous.NextWriter = channel.Writer;
            next.PreviousReader = channel.Reader;
            Task.Run(next.Handle);

            Channel<Workflow<T2>> channel2 = Channel.CreateBounded<Workflow<T2>>(boundedChannelOptions);
            previous.NextWriter2 = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            Task.Run(next.Handle2);

            Channel<Workflow<T3>> channel3 = Channel.CreateBounded<Workflow<T3>>(boundedChannelOptions);
            previous.NextWriter3 = channel3.Writer;
            next.PreviousReader3 = channel3.Reader;
            Task.Run(next.Handle3);

            this._logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous.GetType().Name, next.GetType().Name);
        }

        /// <summary>
        /// 构建两个前处理器到一个后处理器的工作流连接
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="channelCapacity">通道容量</param>
        /// <param name="previous1">第一个前处理器</param>
        /// <param name="previous2">第二个前处理器</param>
        /// <param name="next">后处理器</param>
        private void BuildHandlersWorkflow<T>(int channelCapacity, IOutHandler<T> previous1, IOutHandler<T> previous2, IInHandler<T, T> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(channelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            Channel<Workflow<T>> channel1 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous1.NextWriter = channel1.Writer;
            next.PreviousReader = channel1.Reader;
            Task.Run(next.Handle);

            Channel<Workflow<T>> channel2 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous2.NextWriter = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            Task.Run(next.Handle2);

            this._logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous1.GetType().Name, next.GetType().Name);
            this._logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous2.GetType().Name, next.GetType().Name);
        }

        /// <summary>
        /// 为处理器调度中止事件处理
        /// </summary>
        /// <param name="handler">基础处理器</param>
        private void ScheduleOnAbort(BaseHandler handler)
        {
            handler.OnAbort += (deviceId, sessionId, message) =>
            {
                this._logger?.LogDebug(Lang.HandlerManager_ScheduleOnAbort_Aborted, deviceId, sessionId, message);
            };
        }
    }
}
