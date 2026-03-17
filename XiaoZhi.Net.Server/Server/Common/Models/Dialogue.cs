using Microsoft.SemanticKernel.ChatCompletion;
using System;

namespace XiaoZhi.Net.Server.Common.Models
{
/// <summary>
/// 表示一个对话实体，包含设备ID、客户端会话ID、角色和内容等信息
/// </summary>
internal class Dialogue
{
    /// <summary>
    /// 初始化Dialogue类的新实例
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="clientSessionId">客户端会话ID</param>
    /// <param name="role">作者角色</param>
    /// <param name="content">对话内容</param>
    public Dialogue(string deviceId, string clientSessionId, AuthorRole role, string content)
    {
        DeviceId = deviceId;
        ClientSessionId = clientSessionId;
        Role = role;
        Content = content;
        CreateTime = DateTime.Now;
    }

    /// <summary>
    /// 获取或设置客户端会话ID
    /// </summary>
    public string ClientSessionId { get; set; }

    /// <summary>
    /// 获取设备ID
    /// </summary>
    public string DeviceId { get; }

    /// <summary>
    /// 获取作者角色
    /// </summary>
    public AuthorRole Role { get; }

    /// <summary>
    /// 获取对话内容
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// 获取创建时间
    /// </summary>
    public DateTime CreateTime { get; }
}
}
