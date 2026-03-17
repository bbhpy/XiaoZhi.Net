using System.Collections.Generic;
using System.Text.Json;

namespace XiaoZhi.Net.Server.Helpers
{
  /// <summary>
/// 配置助手类，提供从配置字典中获取值的扩展方法
/// </summary>
internal static class ConfigHelper
{
    /// <summary>
    /// 从配置字典中获取指定键对应的字符串值，如果配置为空、键为空或不存在则返回默认值
    /// </summary>
    /// <param name="config">配置字典</param>
    /// <param name="key">要获取值的键</param>
    /// <returns>键对应的值，如果不存在则返回null</returns>
    public static string? GetConfigValueOrDefault(this IDictionary<string, string> config, string key)
    {
        if (config == null || string.IsNullOrEmpty(key) || !config.TryGetValue(key, out string? value))
        {
            return default;
        }
        return value;
    }

    /// <summary>
    /// 从配置字典中获取指定键对应的值并反序列化为目标类型，如果配置为空、键为空或不存在则返回默认值
    /// </summary>
    /// <typeparam name="TValue">目标类型</typeparam>
    /// <param name="config">配置字典</param>
    /// <param name="key">要获取值的键</param>
    /// <returns>反序列化后的值，如果不存在或反序列化失败则返回类型的默认值</returns>
    public static TValue? GetConfigValueOrDefault<TValue>(this IDictionary<string, string> config, string key)
    {
        if (config == null || string.IsNullOrEmpty(key) || !config.TryGetValue(key, out string? value))
        {
            return default;
        }

        if (string.IsNullOrEmpty(value))
        {
            return default;
        }
        return JsonSerializer.Deserialize<TValue>(value, JsonHelper.OPTIONS);
    }

    /// <summary>
    /// 从配置字典中获取指定键对应的值并反序列化为目标类型，如果配置为空、键为空或不存在则返回提供的默认值
    /// </summary>
    /// <typeparam name="TValue">目标类型</typeparam>
    /// <param name="config">配置字典</param>
    /// <param name="key">要获取值的键</param>
    /// <param name="defaultValue">当键不存在或值为空时返回的默认值</param>
    /// <returns>反序列化后的值，如果不存在或反序列化失败则返回提供的默认值</returns>
    public static TValue GetConfigValueOrDefault<TValue>(this IDictionary<string, string> config, string key, TValue defaultValue)
    {
        if (config == null || string.IsNullOrEmpty(key) || !config.TryGetValue(key, out string? value))
        {
            return defaultValue;
        }

        // 检查获取到的值是否为空
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        // 尝试反序列化值，如果失败则返回默认值
        return JsonSerializer.Deserialize<TValue>(value, JsonHelper.OPTIONS) ?? defaultValue;
    }
}
}
