using Microsoft.SemanticKernel;
using System;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    /// <summary>
    /// 私有提供者 - 负责管理和提供各种音频处理、AI服务和客户端组件的容器类
    /// </summary>
    internal class PrivateProvider
    {
        /// <summary>
        /// 内核实例，用于核心业务逻辑处理
        /// </summary>
        private Kernel? _kernel;

        /// <summary>
        /// 物联网客户端，用于物联网设备通信
        /// </summary>
        private IIoTClient? _iotClient;
        /// <summary>
        /// MCP客户端，用于MCP设备通信
        /// </summary>
        private IMcpClient? _mcpClient;
        /// <summary>
        /// 音频处理器，用于处理音频数据
        /// </summary>
        private IAudioProcessor? _audioProcessor;
        /// <summary>
        /// 音频播放器客户端，用于播放音频数据
        /// </summary>
        private IAudioPlayerClient? _audioPlayerClient;

        /// <summary>
        /// 音频解码器，用于音频数据解码
        /// </summary>
        public IAudioDecoder? AudioDecoder { get; private set; }

        /// <summary>
        /// 语音活动检测器，用于检测语音信号中的活动部分
        /// </summary>
        public IVad? Vad { get; private set; }

        /// <summary>
        /// 自动语音识别器，用于将语音转换为文本
        /// </summary>
        public IAsr? Asr { get; private set; }

        /// <summary>
        /// 大语言模型，用于自然语言处理和生成
        /// </summary>
        public ILlm? Llm { get; private set; }

        /// <summary>
        /// 文本转语音合成器，用于将文本转换为语音
        /// </summary>
        public ITts? Tts { get; private set; }

        /// <summary>
        /// 音频重采样器，用于音频采样率转换
        /// </summary>
        public IAudioResampler? AudioResampler { get; private set; }

        /// <summary>
        /// 音频编码器，用于音频数据编码
        /// </summary>
        public IAudioEncoder? AudioEncoder { get; private set; }

        /// <summary>
        /// 获取内核实例
        /// </summary>
        public Kernel? Kernel => this._kernel;

        /// <summary>
        /// 指示是否已设置物联网客户端
        /// </summary>
        public bool HasIoT { get; private set; }

        /// <summary>
        /// 获取物联网客户端实例
        /// </summary>
        public IIoTClient? IoTClient => this._iotClient;

        /// <summary>
        /// 获取MCP客户端实例
        /// </summary>
        public IMcpClient? McpClient => this._mcpClient;

        /// <summary>
        /// 获取音频处理器实例
        /// </summary>
        public IAudioProcessor? AudioProcessor => this._audioProcessor;

        /// <summary>
        /// 获取音频播放器客户端实例
        /// </summary>
        public IAudioPlayerClient? AudioPlayerClient => this._audioPlayerClient;

        /// <summary>
        /// 设置音频解码器
        /// </summary>
        /// <param name="audioDecoder">音频解码器实例</param>
        public void SetAudioDecoder(IAudioDecoder audioDecoder)
        {
            this.AudioDecoder = audioDecoder;
        }

        /// <summary>
        /// 设置语音活动检测器
        /// </summary>
        /// <param name="vad">语音活动检测器实例</param>
        public void SetVad(IVad vad)
        {
            this.Vad = vad;
        }

        /// <summary>
        /// 设置自动语音识别器
        /// </summary>
        /// <param name="asr">自动语音识别器实例</param>
        public void SetAsr(IAsr asr)
        {
            this.Asr = asr;
        }

        /// <summary>
        /// 设置大语言模型
        /// </summary>
        /// <param name="llm">大语言模型实例</param>
        public void SetLlm(ILlm llm)
        {
            this.Llm = llm;
        }

        /// <summary>
        /// 设置文本转语音合成器
        /// </summary>
        /// <param name="tts">文本转语音合成器实例</param>
        public void SetTts(ITts tts)
        {
            this.Tts = tts;
        }

        /// <summary>
        /// 设置音频重采样器
        /// </summary>
        /// <param name="audioResampler">音频重采样器实例</param>
        public void SetAudioResampler(IAudioResampler audioResampler)
        {
            this.AudioResampler = audioResampler;
        }

        /// <summary>
        /// 设置音频编码器
        /// </summary>
        /// <param name="audioEncoder">音频编码器实例</param>
        public void SetAudioEncoder(IAudioEncoder audioEncoder)
        {
            this.AudioEncoder = audioEncoder;
        }

        /// <summary>
        /// 设置内核实例
        /// </summary>
        /// <param name="kernel">内核实例</param>
        public void SetKernel(Kernel kernel)
        {
            this._kernel = kernel;
        }

        /// <summary>
        /// 设置物联网客户端
        /// </summary>
        /// <param name="iotClient">物联网客户端实例</param>
        public void SetIoTClient(IIoTClient iotClient)
        {
            this._iotClient = iotClient;
            this.HasIoT = true;
        }

        /// <summary>
        /// 设置MCP客户端
        /// </summary>
        /// <param name="mcpClient">MCP客户端实例</param>
        public void SetMcpClient(IMcpClient mcpClient)
        {
            this._mcpClient = mcpClient;
        }

        /// <summary>
        /// 设置音频播放器客户端
        /// </summary>
        /// <param name="audioPlayer">音频播放器客户端实例</param>
        public void SetAudioPlayerClient(IAudioPlayerClient audioPlayer)
        {
            this._audioPlayerClient = audioPlayer;
        }

        /// <summary>
        /// 设置音频处理器
        /// </summary>
        /// <param name="audioProcessor">音频处理器实例</param>
        public void SetAudioProcessor(IAudioProcessor audioProcessor)
        {
            this._audioProcessor = audioProcessor;
        }

        /// <summary>
        /// 释放所有资源，包括VAD、ASR、TTS等组件的非Sherpa模型实例以及各种客户端和处理器
        /// </summary>
        public void Release()
        {
            // 释放非Sherpa模型的VAD、ASR、TTS组件
            if (this.Vad is not null && !this.Vad.IsSherpaModel)
            {
                this.Vad.Dispose();
            }
            if (this.Asr is not null && !this.Asr.IsSherpaModel)
            {
                this.Asr.Dispose();
            }
            if (this.Tts is not null && !this.Tts.IsSherpaModel)
            {
                this.Tts.Dispose();
            }

            // 释放其他可释放的组件
            this.AudioResampler?.Dispose();
            this.AudioEncoder?.Dispose();
            this._iotClient?.Dispose();
            this._mcpClient?.Dispose();
            this._audioPlayerClient?.Dispose();
            this._audioProcessor?.Dispose();
            this._kernel = null;
        }
    }
}
