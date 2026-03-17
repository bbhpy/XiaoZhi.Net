namespace XiaoZhi.Net.Server.Common.Models
{
/// <summary>
/// 表示一个Hello消息对象，用于建立会话连接时的消息传输
/// </summary>
internal class HelloMessage
{
    /// <summary>
    /// 初始化HelloMessage类的新实例
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="transport">传输协议类型</param>
    /// <param name="audioParams">音频设置参数</param>
    public HelloMessage(string sessionId, string transport, AudioSetting audioParams)
    {
        this.SessionId = sessionId;
        this.Transport = transport;
        this.AudioParams = audioParams;
    }

    /// <summary>
    /// 获取消息类型，固定返回"hello"
    /// </summary>
    public string Type => "hello";

    /// <summary>
    /// 获取或设置协议版本，默认为1
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 获取或设置传输协议类型
    /// </summary>
    public string Transport { get; set; }

    /// <summary>
    /// 获取或设置音频配置参数
    /// </summary>
    public AudioSetting AudioParams { get; set; }

    /// <summary>
    /// 获取或设置会话标识符
    /// </summary>
    public string SessionId { get; set; }
}
}
