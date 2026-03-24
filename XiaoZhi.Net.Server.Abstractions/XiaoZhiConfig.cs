using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server
{
    /// <summary>
    /// 系统配置类，包含服务器协议、日志设置、WebSocket服务器设置、音乐提供器设置、设备绑定设置、已选设置、已配置设置、音频设置等
    /// </summary>
    public sealed class XiaoZhiConfig
    {
        /// <summary>
        /// 服务器协议
        /// </summary>
        public ServerProtocol ServerProtocol { get; set; }
        /// <summary>
        /// 提示语 对于角色的提示语，通常用于LLM提供器，作为系统提示语的一部分，帮助模型更好地理解角色设定和对话背景，从而生成更符合预期的回复
        /// </summary>
        public string Prompt { get; set; } = string.Empty;
        /// <summary>
        /// 认证是否启用
        /// </summary>
        public bool AuthEnabled { get; set; }
        /// <summary>
        /// 日志设置
        /// </summary>
        public LogSetting LogSetting { get; set; } = new LogSetting();
        /// <summary>
        /// WebSocket服务器设置
        /// </summary>
        public WebSocketServerOption WebSocketServerOption { get; set; } = new WebSocketServerOption();
        /// <summary>
        /// 音乐提供器设置
        /// </summary>
        public MusicProviderSetting MusicProviderSetting { get; set; } = new MusicProviderSetting();
        /// <summary>
        ///  设备绑定设置
        /// </summary>
        public DeviceBindSetting DeviceBindSetting { get; set; } = new DeviceBindSetting();
        /// <summary>
        /// 已选设置  已选设置是一个字典结构，用于存储用户选择的配置项，帮助系统在运行过程中能够快速地访问和使用这些配置项，从而提供更好的用户体验和功能支持
        /// </summary>
        public Dictionary<string, string> SelectedSettings { get; set; } = new Dictionary<string, string>();
        /// <summary>
        ///  已配置设置  已配置设置是一个嵌套的字典结构，用于存储不同类别和类型的配置信息，帮助系统在运行过程中能够灵活地访问和管理各种配置项，从而提供更好的功能支持和用户体验
        /// </summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> ConfiguredSettings { get; set; } = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        /// <summary>
        ///  音频设置  音频设置类，包含音频格式、采样率、声道数和帧持续时间等相关配置，帮助系统在处理音频数据时，能够正确地转换和处理音频数据，从而提供更好的音频播放体验
        /// </summary>
        public AudioSetting AudioSetting { get; set; } = null!;
        /// <summary>
        /// 多模态处理设置 多模态处理设置类，包含多模态处理所需的配置信息，帮助系统在处理多模态数据时，能够正确地转换和处理数据，从而提供更好的多模态体验
        /// </summary>
        public Dictionary<string, ModelSetting>? McpSettings { get; set; }

        /// <summary>
        /// MQTT服务端配置项
        /// </summary>
        public MqttServerConfig MqttConfig { get; set; } = new MqttServerConfig();

        /// <summary>
        /// udp服务端配置项
        /// </summary>
        public UdpAudioConfig UdpConfig { get; set; } = new UdpAudioConfig();

        public McpServerEndpointconfig McpServerEndpointConfig { get; set; } = new McpServerEndpointconfig();
    }

    #region Log
    /// <summary>
    /// 日志设置 日志设置类，包含日志级别、日志文件路径、输出模板和保留文件数量限制等相关配置，帮助系统在运行过程中记录和管理日志信息，从而便于调试、监控和维护
    /// </summary>
    public sealed class LogSetting
    {
        /// <summary>
        /// 获取或设置日志级别，默认为"INFO"
        /// </summary>
        public string LogLevel { get; set; } = "INFO";

        /// <summary>
        /// 获取或设置日志文件保存路径，默认为"logs/server_log.log"
        /// </summary>
        public string LogFilePath { get; set; } = "logs/server_log.log";

        /// <summary>
        /// 获取或设置日志输出模板格式，默认包含时间戳、日志级别、消息内容和异常信息
        /// </summary>
        public string OutputTemplate { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}";

        /// <summary>
        /// 获取或设置保留的日志文件数量限制，默认为7个
        /// </summary>
        public int RetainedFileCountLimit { get; set; } = 7;
    }
    #endregion

    #region WebSocketSetting
    /// <summary>
    /// WebSocket服务器配置选项类
    /// </summary>
    public sealed class WebSocketServerOption
    {
        /// <summary>
        /// 服务器监听IP地址，默认为"0.0.0.0"（监听所有网络接口）
        /// </summary>
        public string IP { get; set; } = "0.0.0.0";

        /// <summary>
        /// 服务器监听端口号，默认为4530
        /// </summary>
        public int Port { get; set; } = 4530;

        /// <summary>
        /// WebSocket连接路径，默认为"/yangai/v1/"
        /// </summary>
        public string Path { get; set; } = "/yangai/v1/";

        /// <summary>
        /// WSS安全连接配置选项，可为空表示使用普通WebSocket连接
        /// </summary>
        public WssOption? WssOption { get; set; }
    }

    /// <summary>
    /// WSS安全连接配置选项类
    /// </summary>
    public sealed class WssOption
    {
        /// <summary>
        /// 证书文件路径
        /// </summary>
        public string CertFilePath { get; set; } = "";

        /// <summary>
        /// 证书密码，可为空
        /// </summary>
        public string? CertPassword { get; set; }
    }
    #endregion
    /// <summary>
    /// 模型设置
    /// </summary>
    public sealed class ModelSetting
    {
        public string ModelName { get; set; } = null!;
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();
    }
    /// <summary>
    /// 音频设置类，包含音频格式、采样率、声道数和帧持续时间等相关配置，帮助系统在处理音频数据时，能够正确地转换和处理音频数据，从而提供更好的音频播放体验
    /// </summary>
    public sealed class AudioSetting
    {
        /// <summary>
        /// 初始化 AudioSetting 类的新实例
        /// </summary>
        public AudioSetting()
        {

        }

        /// <summary>
        /// 使用指定的音频参数初始化 AudioSetting 类的新实例
        /// </summary>
        /// <param name="format">音频编码格式</param>
        /// <param name="sampleRate">采样率（Hz）</param>
        /// <param name="channels">声道数</param>
        /// <param name="frameDuration">帧时长（毫秒）</param>
        public AudioSetting(string format, int sampleRate, int channels, int frameDuration)
        {
            this.Format = format;
            this.SampleRate = sampleRate;
            this.Channels = channels;
            this.FrameDuration = frameDuration;
        }

        /// <summary>
        /// 获取或设置音频编码格式，默认为"opus"
        /// </summary>
        public string Format { get; set; } = "opus";

        /// <summary>
        /// 获取或设置音频采样率（Hz），默认为16000
        /// </summary>
        public int SampleRate { get; set; } = 16000;

        /// <summary>
        /// 获取或设置音频声道数，默认为1
        /// </summary>
        public int Channels { get; set; } = 1;

        /// <summary>
        /// 获取或设置音频帧时长（毫秒），默认为60
        /// </summary>
        public int FrameDuration { get; set; } = 60;

        /// <summary>
        /// 计算并获取每帧的样本大小
        /// 计算公式：采样率 × 帧时长 × 声道数 ÷ 1000
        /// </summary>
        public int FrameSize => this.SampleRate * this.FrameDuration * this.Channels / 1000;
    }
    /// <summary>
    /// 音乐提供器设置  音乐文件夹路径配置，帮助系统在处理与音乐相关的请求时，能够正确地定位和访问存储音乐文件的目录，从而提供更好的音乐播放体验
    /// </summary>
    public sealed class MusicProviderSetting
    {
        public string MusicFolderPath { get; set; } = "musics";
    }
    /// <summary>
    /// 设备绑定语音配置类  绑定码提示音、绑定码数字语音、绑定未找到提示音等相关配置，帮助系统在设备绑定过程中提供更好的用户体验和交互反馈
    /// </summary>
    public sealed class DeviceBindSetting
    {
        /// <summary>
        /// 绑定码提示文件路径
        /// </summary>
        public string BindCodePromptFilePath { get; set; } = "configs/assets/bind_code.wav";
        /// <summary>
        ///  绑定码数字文件夹路径
        /// </summary>
        public string BindCodeDigitFolderPath { get; set; } = "configs/assets/bind_code";
        /// <summary>
        ///  绑定未找到提示文件路径
        /// </summary>
        public string BindNotFoundFilePath { get; set; } = "configs/assets/bind_not_found.wav";
    }
    /// <summary>
    /// MQTT服务端配置类
    /// 作用：定义MQTT服务端的所有可配置参数，适配MQTTnet 5.1.0.1559版本
    /// </summary>
    public class MqttServerConfig
    {
        /// <summary>
        /// MQTT服务监听端口（默认：1883，MQTT标准端口）
        /// </summary>
        public int Port { get; set; } = 1883;
        /// <summary>
        /// 
        /// </summary>
        public int TlsPort { get; set; } = 8883;
        /// <summary>
        /// 是否启用TLS加密（默认：false，生产环境建议开启）
        /// </summary>
        public bool UseTls { get; set; } = false;

        /// <summary>
        /// TLS证书路径（UseTls为true时必填）
        /// 示例："certificates/mqtt_server_cert.pfx"
        /// </summary>
        public string TlsCertPath { get; set; } = string.Empty;

        /// <summary>
        /// TLS证书密码（UseTls为true且证书有密码时必填）
        /// </summary>
        public string TlsCertPassword { get; set; } = string.Empty;

        /// <summary>
        /// 全局认证用户名（所有MQTT客户端共用，默认：xiaozhi_mqtt）
        /// </summary>
        public string GlobalUsername { get; set; } = "yangai_mqtt";

        /// <summary>
        /// 全局认证密码（默认：xiaozhi@123456）
        /// </summary>
        public string GlobalPassword { get; set; } = "yangai@123456";

        /// <summary>
        /// 连接请求队列大小（默认：1000，适配MQTTnet的WithConnectionBacklog方法）
        /// </summary>
        public int ConnectionBacklog { get; set; } = 1000;

        /// <summary>
        /// 是否启用持久会话（默认：true，适配MQTTnet的EnablePersistentSessions属性）
        /// </summary>
        public bool EnablePersistentSessions { get; set; } = true;

        /// <summary>
        /// 心跳监控间隔（秒，默认：30，适配MQTTnet的KeepAliveOptions.MonitorInterval）
        /// </summary>
        public int KeepAliveMonitorInterval { get; set; } = 30;

        /// <summary>
        /// 读取客户端负载时是否断开超时连接（默认：false，适配KeepAliveOptions）
        /// </summary>
        public bool DisconnectClientWhenReadingPayload { get; set; } = false;

        /// <summary>
        /// 每个客户端最大待处理消息数（默认：1000，适配MaxPendingMessagesPerClient）
        /// </summary>
        public int MaxPendingMessagesPerClient { get; set; } = 1000;

    }

    public class McpServerEndpointconfig
    {
        public int Port { get; set; } = 4531;
        public string Path { get; set; } = "/mcp";
    }

    /// <summary>
    /// UDP音频配置
    /// </summary>
    public class UdpAudioConfig
    {
        /// <summary>
        /// UDP服务器绑定地址
        /// </summary>
        public string Server { get; set; } = "0.0.0.0";

        /// <summary>
        /// UDP监听端口
        /// </summary>
        public int Port { get; set; } = 8888;

        /// <summary>
        /// 128位AES密钥（十六进制字符串）
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// SHA1哈希盐值（生成nonce用）
        /// </summary>
        public string NonceSalt { get; set; } = "UDP_AUDIO_NONCE_SALT_2026";

        /// <summary>
        /// UDP接收缓冲区大小（8KB）
        /// </summary>
        public int ReceiveBufferSize => 8192;
        public int UdpSessionTimeoutSeconds { get; set; } = 60; // UDP会话超时时间
        /// <summary>
        /// CPU 核心数 默认 2
        /// </summary>
        public int WorkerCount { get; set; } = 2;  // 0 表示 CPU 核心数
        /// <summary>
        /// 每个 Worker 的队列容量   默认1000
        /// </summary>
        public int QueueSize { get; set; } = 1000; // 每个 Worker 的队列容量
    }
}
