using System;

namespace XiaoZhi.Net.Server.Resources
{
/// <summary>
/// 定义资源管理接口，用于处理具有特定设置类型的资源加载和释放
/// </summary>
/// <typeparam name="TSettings">资源设置类型，必须为引用类型</typeparam>
internal interface IResource<TSettings> : IDisposable where TSettings : class
{
    /// <summary>
    /// 获取资源名称
    /// </summary>
    string ResourceName { get; }
    
    /// <summary>
    /// 根据指定的设置加载资源
    /// </summary>
    /// <param name="settings">资源加载所需的设置对象</param>
    /// <returns>加载成功返回true，否则返回false</returns>
    bool Load(TSettings settings);
}
}
