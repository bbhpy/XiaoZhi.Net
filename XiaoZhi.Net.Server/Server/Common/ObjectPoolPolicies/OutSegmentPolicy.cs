using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
/// <summary>
/// OutSegment对象池策略类，用于创建和回收OutSegment对象
/// </summary>
internal class OutSegmentPolicy : PooledObjectPolicy<OutSegment>
{
    /// <summary>
    /// 创建新的OutSegment对象
    /// </summary>
    /// <returns>新创建的OutSegment实例</returns>
    public override OutSegment Create()
    {
        return new OutSegment();
    }

    /// <summary>
    /// 将OutSegment对象归还到对象池
    /// </summary>
    /// <param name="obj">需要归还的OutSegment对象</param>
    /// <returns>始终返回true，表示对象可以被回收</returns>
    public override bool Return(OutSegment obj)
    {
        // 重置对象状态以便复用
        obj.Reset();
        return true;
    }
}
}