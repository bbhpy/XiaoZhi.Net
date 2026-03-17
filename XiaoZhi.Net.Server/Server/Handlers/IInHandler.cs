using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
/// <summary>
/// 定义一个处理单一输入类型的处理器接口
/// </summary>
/// <typeparam name="TIn">输入数据的类型</typeparam>
internal interface IInHandler<TIn> : IHandler
{
    /// <summary>
    /// 异步处理工作流数据
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task Handle();
    
    /// <summary>
    /// 获取或设置前一个工作流读取器，用于读取指定类型的输入数据
    /// </summary>
    ChannelReader<Workflow<TIn>> PreviousReader { get; set; }
}

/// <summary>
/// 定义一个处理两个输入类型的处理器接口，继承自IInHandler<TIn1>
/// </summary>
/// <typeparam name="TIn1">第一个输入数据的类型</typeparam>
/// <typeparam name="TIn2">第二个输入数据的类型</typeparam>
internal interface IInHandler<TIn1, TIn2> : IInHandler<TIn1>
{
    /// <summary>
    /// 异步处理第二个输入类型的工作流数据
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task Handle2();
    
    /// <summary>
    /// 获取或设置第二个前一个工作流读取器，用于读取第二个输入类型的数据
    /// </summary>
    ChannelReader<Workflow<TIn2>> PreviousReader2 { get; set; }
}

/// <summary>
/// 定义一个处理三个输入类型的处理器接口，继承自IInHandler<TIn1, TIn2>
/// </summary>
/// <typeparam name="TIn1">第一个输入数据的类型</typeparam>
/// <typeparam name="TIn2">第二个输入数据的类型</typeparam>
/// <typeparam name="TIn3">第三个输入数据的类型</typeparam>
internal interface IInHandler<TIn1, TIn2, TIn3> : IInHandler<TIn1, TIn2>
{
    /// <summary>
    /// 异步处理第三个输入类型的工作流数据
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task Handle3();
    
    /// <summary>
    /// 获取或设置第三个前一个工作流读取器，用于读取第三个输入类型的数据
    /// </summary>
    ChannelReader<Workflow<TIn3>> PreviousReader3 { get; set; }
}
}
