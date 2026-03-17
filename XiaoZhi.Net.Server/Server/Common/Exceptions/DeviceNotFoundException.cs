using System;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
/// <summary>
/// 设备未找到异常类，用于在设备不存在或无法访问时抛出异常  还没实现
/// </summary>
internal class DeviceNotFoundException : Exception
{
    /// <summary>
    /// 初始化 DeviceNotFoundException 类的新实例
    /// </summary>
    public DeviceNotFoundException() : base()
    {
        
    }
    
    /// <summary>
    /// 使用指定的错误消息初始化 DeviceNotFoundException 类的新实例
    /// </summary>
    /// <param name="message">描述错误的错误消息</param>
    public DeviceNotFoundException(string message) : base(message)
    {

    }
    
    /// <summary>
    /// 使用指定的错误消息和内部异常初始化 DeviceNotFoundException 类的新实例
    /// </summary>
    /// <param name="message">描述错误的错误消息</param>
    /// <param name="innerException">导致当前异常的异常</param>
    public DeviceNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
}
}
