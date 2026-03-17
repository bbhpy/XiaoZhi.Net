using System;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
/// <summary>
/// 处理器接口，定义了处理器的基本功能和生命周期管理
/// </summary>
internal interface IHandler : IDisposable
{
    /// <summary>
    /// 获取处理器是否已构建完成的状态
    /// </summary>
    bool Builded { get; }
    
    /// <summary>
    /// 构建处理器
    /// </summary>
    /// <param name="privateProvider">私有提供者实例</param>
    /// <returns>构建成功返回true，否则返回false</returns>
    bool Build(PrivateProvider privateProvider);
    
    /// <summary>
    /// 获取或设置业务发送外部处理器
    /// </summary>
    IBizSendOutter SendOutter { get; set; }
}
}
