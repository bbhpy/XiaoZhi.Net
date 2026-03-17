using System.Threading.Channels;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
/// <summary>
/// 表示一个输出处理器接口，用于处理单一类型输出的工作流
/// </summary>
/// <typeparam name="TOut">输出数据的类型</typeparam>
internal interface IOutHandler<TOut> : IHandler
{
    /// <summary>
    /// 获取或设置下一个工作流通道写入器
    /// </summary>
    ChannelWriter<Workflow<TOut>> NextWriter { get; set; }
}

/// <summary>
/// 表示一个输出处理器接口，用于处理两种不同类型输出的工作流
/// </summary>
/// <typeparam name="TOut1">第一种输出数据的类型</typeparam>
/// <typeparam name="TOut2">第二种输出数据的类型</typeparam>
internal interface IOutHandler<TOut1, TOut2> : IOutHandler<TOut1>
{
    /// <summary>
    /// 获取或设置第二个工作流通道写入器
    /// </summary>
    ChannelWriter<Workflow<TOut2>> NextWriter2 { get; set; }
}

/// <summary>
/// 表示一个输出处理器接口，用于处理三种不同类型输出的工作流
/// </summary>
/// <typeparam name="TOut1">第一种输出数据的类型</typeparam>
/// <typeparam name="TOut2">第二种输出数据的类型</typeparam>
/// <typeparam name="TOut3">第三种输出数据的类型</typeparam>
internal interface IOutHandler<TOut1, TOut2, TOut3> : IOutHandler<TOut1, TOut2>
{
    /// <summary>
    /// 获取或设置第三个工作流通道写入器
    /// </summary>
    ChannelWriter<Workflow<TOut3>> NextWriter3 { get; set; }
}
}
