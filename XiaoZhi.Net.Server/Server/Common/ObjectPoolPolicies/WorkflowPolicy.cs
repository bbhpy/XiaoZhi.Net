using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
/// <summary>
/// 工作流对象池策略类，用于管理工作流对象的创建和回收
/// </summary>
/// <typeparam name="T">工作流处理的数据类型，必须为引用类型</typeparam>
internal class WorkflowPolicy<T> : PooledObjectPolicy<Workflow<T>> where T : class
{
    /// <summary>
    /// 创建新的工作流对象
    /// </summary>
    /// <returns>新创建的工作流对象实例</returns>
    public override Workflow<T> Create()
    {
        return new Workflow<T>();
    }

    /// <summary>
    /// 将工作流对象归还到对象池
    /// </summary>
    /// <param name="obj">需要归还的工作流对象</param>
    /// <returns>始终返回true，表示对象可以安全归还到池中</returns>
    public override bool Return(Workflow<T> obj)
    {
        // 重置工作流对象状态以供下次使用
        obj.Reset();
        return true;
    }
}
}