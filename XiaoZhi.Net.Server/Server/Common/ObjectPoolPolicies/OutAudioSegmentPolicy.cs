using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
/// <summary>
/// 音频段对象池策略类，用于管理OutAudioSegment对象的创建和回收
/// </summary>
internal class OutAudioSegmentPolicy : PooledObjectPolicy<OutAudioSegment>
{
    /// <summary>
    /// 创建新的OutAudioSegment对象
    /// </summary>
    /// <returns>新创建的OutAudioSegment实例</returns>
    public override OutAudioSegment Create()
    {
        return new OutAudioSegment();
    }

    /// <summary>
    /// 将OutAudioSegment对象归还到对象池
    /// </summary>
    /// <param name="obj">需要归还的对象</param>
    /// <returns>始终返回true，表示对象可以安全归还到池中</returns>
    public override bool Return(OutAudioSegment obj)
    {
        // 重置对象状态以供下次使用
        obj.Reset();
        return true;
    }
}
}