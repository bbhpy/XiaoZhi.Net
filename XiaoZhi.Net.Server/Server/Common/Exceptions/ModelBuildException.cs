using System;

namespace XiaoZhi.Net.Server.Common.Exceptions
{
/// <summary>
/// 模型构建异常类，用于在模型构建过程中发生错误时抛出异常
/// </summary>
public class ModelBuildException : Exception
{
    /// <summary>
    /// 初始化 ModelBuildException 类的新实例
    /// </summary>
    public ModelBuildException() { }
    
    /// <summary>
    /// 使用指定的错误消息初始化 ModelBuildException 类的新实例
    /// </summary>
    /// <param name="errorMessage">描述错误的文本消息</param>
    public ModelBuildException(string errorMessage) : base(errorMessage)
    { }

}
}
