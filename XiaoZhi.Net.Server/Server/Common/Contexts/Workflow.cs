namespace XiaoZhi.Net.Server.Common.Contexts
{
   /// <summary>
/// 表示一个工作流类，用于管理会话、设备、轮次ID和泛型数据的工作流状态
/// </summary>
/// <typeparam name="T">工作流中存储的数据类型</typeparam>
internal class Workflow<T>
{
    private string _sessionId = null!;
    private string _deviceId = null!;
    private long _turnId;
    private T _data = default!;

    /// <summary>
    /// 初始化 Workflow 类的新实例
    /// </summary>
    public Workflow()
    {
    }

    /// <summary>
    /// 获取当前工作流的会话ID
    /// </summary>
    public string SessionId => this._sessionId;

    /// <summary>
    /// 获取当前工作流的设备ID
    /// </summary>
    public string DeviceId => this._deviceId;

    /// <summary>
    /// 获取当前工作流的轮次ID
    /// </summary>
    public long TurnId => this._turnId;

    /// <summary>
    /// 获取当前工作流的数据
    /// </summary>
    public T Data => this._data;

    /// <summary>
    /// 使用指定的参数初始化工作流
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="deviceId">设备ID</param>
    /// <param name="data">要存储的泛型数据</param>
    /// <param name="turnId">轮次ID</param>
    public void Initialize(string sessionId, string deviceId, T data, long turnId)
    {
        this._sessionId = sessionId;
        this._deviceId = deviceId;
        this._data = data;
        this._turnId = turnId;
    }

    /// <summary>
    /// 使用会话上下文和数据初始化工作流
    /// </summary>
    /// <param name="context">包含会话信息的Session对象</param>
    /// <param name="data">要存储的泛型数据</param>
    public void Initialize(Session context, T data)
    {
        this._sessionId = context.SessionId;
        this._deviceId = context.DeviceId;
        this._data = data;
        this._turnId = context.TurnId;
    }

    /// <summary>
    /// 重置工作流的状态，将所有字段恢复到初始状态
    /// </summary>
    public void Reset()
    {
        this._sessionId = null!;
        this._deviceId = null!;
        this._data = default!;
        this._turnId = 0;
    }
}
}
