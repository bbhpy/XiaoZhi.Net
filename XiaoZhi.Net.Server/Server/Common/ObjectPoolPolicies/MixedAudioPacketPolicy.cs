using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
/// <summary>
/// 混合音频包对象池策略类，用于管理MixedAudioPacket对象的创建和回收
/// </summary>
internal class MixedAudioPacketPolicy : PooledObjectPolicy<MixedAudioPacket>
{
    /// <summary>
    /// 创建一个新的MixedAudioPacket对象
    /// </summary>
    /// <returns>新创建的MixedAudioPacket实例</returns>
    public override MixedAudioPacket Create()
    {
        return new MixedAudioPacket();
    }

    /// <summary>
    /// 将MixedAudioPacket对象归还到对象池
    /// </summary>
    /// <param name="obj">需要归还的MixedAudioPacket对象</param>
    /// <returns>始终返回true，表示对象可以安全归还到池中</returns>
    public override bool Return(MixedAudioPacket obj)
    {
        // 重置对象状态以供下次使用
        obj.Reset();
        return true;
    }
}
}