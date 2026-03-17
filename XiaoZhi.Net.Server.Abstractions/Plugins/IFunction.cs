namespace XiaoZhi.Net.Server.Abstractions
{
/// <summary>
/// 定义函数接口，用于描述具有名称、描述和方法委托的功能组件
/// </summary>
public interface IFunction
{
    /// <summary>
    /// 获取函数的名称
    /// </summary>
    string FunctionName { get; }
    
    /// <summary>
    /// 获取函数的描述信息
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 获取函数的方法委托实现
    /// </summary>
    Delegate Method { get; }
}
}
