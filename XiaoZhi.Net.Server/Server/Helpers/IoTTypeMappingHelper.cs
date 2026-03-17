using System;

namespace XiaoZhi.Net.Server.Helpers
{
 /// <summary>
/// 提供IoT类型映射相关的辅助方法，用于将字符串描述转换为对应的.NET类型、获取默认值以及进行值转换
/// </summary>
internal static class IoTTypeMappingHelper
{
    /// <summary>
    /// 根据类型描述字符串获取对应的.NET类型
    /// </summary>
    /// <param name="typeDescription">类型描述字符串，如"number"、"boolean"等</param>
    /// <returns>对应的.NET类型，如果描述不匹配则返回string类型</returns>
    public static Type GetIoTType(string typeDescription)
    {
        return typeDescription.ToLower() switch
        {
            "number" => typeof(decimal),
            "boolean" => typeof(bool),
            _ => typeof(string),
        };
    }

    /// <summary>
    /// 根据类型描述字符串获取该类型的默认值
    /// </summary>
    /// <param name="typeDescription">类型描述字符串，如"number"、"boolean"等</param>
    /// <returns>对应类型的默认值，如果描述不匹配则返回空字符串</returns>
    public static object GetDefaultValue(string typeDescription)
    {
        return typeDescription.ToLower() switch
        {
            "number" => 0m,
            "boolean" => false,
            _ => "",
        };
    }

    /// <summary>
    /// 将给定的值转换为目标类型
    /// </summary>
    /// <param name="value">要转换的值，可以为null</param>
    /// <param name="type">目标类型，可以为null</param>
    /// <returns>转换后的值，如果类型为null则直接返回原值</returns>
    public static object? ConvertValue(object? value, Type? type)
    {
        // 如果目标类型为空，则直接返回原值
        if (type is null)
        {
            return value;
        }
        return type switch
        {
            Type t when t == typeof(decimal) => Convert.ToDecimal(value),
            Type t when t == typeof(bool) => Convert.ToBoolean(value),
            _ => value,
        };
    }
}
}
