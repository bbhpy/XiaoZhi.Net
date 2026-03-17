using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    /// <summary>
    /// 处理管道类，用于管理和执行不同类型的消息处理器
    /// </summary>
    internal class HandlerPipeline
    {
        /// <summary>
        /// 处理Hello消息的处理器实例，负责处理初始连接时的Hello消息
        /// </summary>
        private HelloMessageHandler? _helloMessageHandler;
        /// <summary>
        /// 处理文本消息的处理器实例，负责处理用户发送的文本消息
        /// </summary>
        private TextHandler? _textHandler;
        /// <summary>
        /// 处理音频接收消息的处理器实例，负责处理用户发送的音频数据
        /// </summary>
        private AudioReceiveHandler? _audioReceiveHandler;
        /// <summary>
        /// 处理文本消息的容器，用于存储文本消息的处理器实例
        ///  </summary>
        private IDictionary<string, IHandler>? _handlerContainer;

        /// <summary>
        /// 初始化Hello消息处理器
        /// </summary>
        /// <param name="helloMessageHandler">Hello消息处理器实例，不能为null</param>
        /// <exception cref="ArgumentNullException">当helloMessageHandler为null时抛出</exception>
        public void InitHelloMessageHandler(HelloMessageHandler helloMessageHandler)
        {
            this._helloMessageHandler = helloMessageHandler ?? throw new ArgumentNullException(nameof(helloMessageHandler));
        }

        /// <summary>
        /// 初始化处理管道，从容器中获取各种处理器
        /// </summary>
        /// <param name="handlerContainer">包含所有处理器的字典容器</param>
        /// <exception cref="ArgumentNullException">当TextHandler或AudioReceiveHandler未找到时抛出</exception>
        public void InitHandlerPipeline(IDictionary<string, IHandler> handlerContainer)
        {
            this._handlerContainer = handlerContainer;
            // 从容器中获取文本处理器
            this._textHandler = handlerContainer[nameof(TextHandler)] as TextHandler ?? throw new ArgumentNullException(nameof(TextHandler));
            // 从容器中获取音频接收处理器
            this._audioReceiveHandler = handlerContainer[nameof(AudioReceiveHandler)] as AudioReceiveHandler ?? throw new ArgumentNullException(nameof(AudioReceiveHandler));
        }

        /// <summary>
        /// 处理Hello消息
        /// </summary>
        /// <param name="helloMessage">Hello消息的JSON对象</param>
        /// <returns>异步操作任务</returns>
        /// <exception cref="InvalidOperationException">当HelloMessageHandler未初始化时抛出</exception>
        public async ValueTask HandleHelloMessage(JsonObject helloMessage)
        {
            if (this._helloMessageHandler is not null)
            {
                await this._helloMessageHandler.Handle(helloMessage);
            }
            else
            {
                throw new InvalidOperationException("HelloMessageHandler 未初始化");
            }
        }

        /// <summary>
        /// 处理文本消息
        /// </summary>
        /// <param name="data">文本数据</param>
        /// <exception cref="InvalidOperationException">当TextHandler未初始化时抛出</exception>
        public void HandleTextMessage(string data)
        {
            if (this._textHandler is not null)
            {
                this._textHandler.Handle(data);
            }
            else
            {
                throw new InvalidOperationException("TextHandler 未初始化");
            }
        }

        /// <summary>
        /// 异步处理二进制消息（音频数据）
        /// </summary>
        /// <param name="data">二进制数据数组</param>
        /// <returns>异步操作任务</returns>
        /// <exception cref="InvalidOperationException">当AudioReceiveHandler未初始化时抛出</exception>
        public async Task HandleBinaryMessageAsync(byte[] data)
        {
            if (this._audioReceiveHandler is not null)
            {
                await this._audioReceiveHandler.Handle(data);
            }
            else
            {
                throw new InvalidOperationException("AudioReceiveHandler 未初始化");
            }
        }

        /// <summary>
        /// 释放所有处理器资源
        /// </summary>
        public void Release()
        {
            if (this._handlerContainer is null || this._handlerContainer.Count == 0)
            {
                return;
            }
            // 遍历并释放所有可释放的处理器
            foreach (IDisposable handler in this._handlerContainer.Values)
            {
                handler.Dispose();
            }
            this._handlerContainer.Clear();
        }


    }
}
