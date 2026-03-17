using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Resources
{
/// <summary>
/// 资源基类，提供资源管理的基础功能
/// </summary>
/// <typeparam name="TLogger">日志记录器类型</typeparam>
/// <typeparam name="TSettings">配置设置类型，必须为引用类型</typeparam>
internal abstract class BaseResource<TLogger, TSettings> : IResource<TSettings> where TSettings : class
{
    /// <summary>
    /// 初始化 BaseResource 类的新实例
    /// </summary>
    /// <param name="logger">用于记录日志的 ILogger 实例</param>
    public BaseResource(ILogger<TLogger> logger)
    {
        this.Logger = logger;
    }

    /// <summary>
    /// 获取资源名称
    /// </summary>
    public abstract string ResourceName { get; }

    /// <summary>
    /// 获取日志记录器
    /// </summary>
    protected ILogger<TLogger> Logger { get; }

    /// <summary>
    /// 加载资源
    /// </summary>
    /// <param name="settings">资源配置对象</param>
    /// <returns>加载成功返回 true，否则返回 false</returns>
    public abstract bool Load(TSettings settings);

    /// <summary>
    /// 释放资源
    /// </summary>
    public abstract void Dispose();
}
}
