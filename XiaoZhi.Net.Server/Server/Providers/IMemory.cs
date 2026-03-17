using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Providers
{
/// <summary>
/// 内存接口，用于管理对话记录的存储和检索
/// 继承自IProvider<ModelSetting>接口
/// </summary>
internal interface IMemory : IProvider<ModelSetting>
{
    /// <summary>
    /// 向指定设备和会话追加对话记录
    /// </summary>
    /// <param name="deviceId">设备标识符</param>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="dialogue">要追加的对话对象</param>
    /// <returns>异步返回布尔值，表示追加操作是否成功</returns>
    Task<bool> AppendDialogue(string deviceId, string sessionId, Dialogue dialogue);

    /// <summary>
    /// 获取指定设备和会话的所有对话记录
    /// </summary>
    /// <param name="deviceId">设备标识符</param>
    /// <param name="sessionId">会话标识符</param>
    /// <returns>异步返回对话集合的可枚举对象</returns>
    Task<IEnumerable<Dialogue>> GetDialogues(string deviceId, string sessionId);
}
}
