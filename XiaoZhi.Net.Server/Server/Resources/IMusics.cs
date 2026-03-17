using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Resources
{
/// <summary>
/// 音乐资源管理接口，继承自IResource接口，用于管理音乐文件相关的操作
/// </summary>
/// <typeparam name="MusicProviderSetting">音乐提供者设置类型</typeparam>
internal interface IMusics : IResource<MusicProviderSetting>
{
    /// <summary>
    /// 获取是否存在音乐文件的标识
    /// </summary>
    /// <returns>如果存在音乐文件则返回true，否则返回false</returns>
    bool HasMusicFiles { get; }

    /// <summary>
    /// 获取音乐文件字典，键为文件名，值为文件路径
    /// </summary>
    /// <returns>只读字典，包含音乐文件名和对应路径的映射关系</returns>
    IReadOnlyDictionary<string, string> MusicFiles { get; }

    /// <summary>
    /// 更新音乐文件列表
    /// </summary>
    /// <returns>更新成功返回true，失败返回false</returns>
    bool UpdateMusicFiles();
}
}
