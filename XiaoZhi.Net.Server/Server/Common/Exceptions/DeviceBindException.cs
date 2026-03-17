using System;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
/// <summary>
/// 设备绑定异常类，用于处理设备绑定过程中出现的异常情况
/// </summary>
internal class DeviceBindException : Exception
{
    /// <summary>
    /// 获取设备绑定码
    /// </summary>
    public string BindCode { get; }

    /// <summary>
    /// 初始化 DeviceBindException 类的新实例
    /// </summary>
    /// <param name="bindCode">设备绑定码</param>
    public DeviceBindException(string bindCode) : base()
    {
        this.BindCode = bindCode;
    }
    
    /// <summary>
    /// 初始化 DeviceBindException 类的新实例
    /// </summary>
    /// <param name="bindCode">设备绑定码</param>
    /// <param name="message">描述错误的消息</param>
    public DeviceBindException(string bindCode, string message) : base(message)
    {
        this.BindCode = bindCode;
    }
    
    /// <summary>
    /// 初始化 DeviceBindException 类的新实例
    /// </summary>
    /// <param name="bindCode">设备绑定码</param>
    /// <param name="message">描述错误的消息</param>
    /// <param name="innerException">导致当前异常的内部异常引用</param>
    public DeviceBindException(string bindCode, string message, Exception innerException) : base(message, innerException)
    {
        this.BindCode = bindCode;
    }
}
}
