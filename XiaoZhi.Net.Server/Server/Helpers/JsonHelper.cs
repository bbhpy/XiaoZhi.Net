using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Server.Helpers
{
 /// <summary>
/// JSON序列化和反序列化辅助类，提供统一的JSON处理选项和方法
/// </summary>
internal static class JsonHelper
{
    /// <summary>
    /// 预定义的JSON序列化选项，配置了驼峰命名转蛇形命名、忽略空值等设置
    /// </summary>
    public static readonly JsonSerializerOptions OPTIONS = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = new JsonSnakeCaseNamingPolicy(),
        DictionaryKeyPolicy = new JsonSnakeCaseNamingPolicy(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 将对象序列化为JSON字符串的扩展方法
    /// </summary>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>序列化后的JSON字符串</returns>
    public static string ToJson(this object obj) => JsonSerializer.Serialize(obj, JsonHelper.OPTIONS);

    /// <summary>
    /// 将对象序列化为JsonNode的扩展方法
    /// </summary>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>序列化后的JsonNode对象，如果输入为null则返回null</returns>
    public static JsonNode? ToNode(this object obj) => obj is null ? null : JsonSerializer.SerializeToNode(obj, JsonHelper.OPTIONS);

    /// <summary>
    /// 将对象序列化为JSON字符串
    /// </summary>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>序列化后的JSON字符串</returns>
    public static string Serialize(object obj) => JsonSerializer.Serialize(obj, JsonHelper.OPTIONS);

    /// <summary>
    /// 将JsonObject序列化为JSON字符串
    /// </summary>
    /// <param name="obj">要序列化的JsonObject</param>
    /// <returns>序列化后的JSON字符串</returns>
    public static string Serialize(JsonObject obj) => obj.ToJsonString(JsonHelper.OPTIONS);

    /// <summary>
    /// 将对象序列化为UTF-8字节数组
    /// </summary>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>序列化后的UTF-8字节数组</returns>
    public static byte[] SerializeToUtf8Bytes(object obj) => JsonSerializer.SerializeToUtf8Bytes(obj, JsonHelper.OPTIONS);

    /// <summary>
    /// 将JSON字符串反序列化为指定类型的对象
    /// </summary>
    /// <typeparam name="T">目标类型，必须是class类型</typeparam>
    /// <param name="json">JSON字符串</param>
    /// <returns>反序列化后的对象，如果发生异常则返回null</returns>
    public static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonHelper.OPTIONS);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 自定义的蛇形命名策略，将驼峰命名转换为下划线分隔的小写命名
/// </summary>
file class JsonSnakeCaseNamingPolicy : JsonNamingPolicy
{
    private readonly string _separator = "_";

    /// <summary>
    /// 将属性名转换为蛇形命名格式
    /// </summary>
    /// <param name="name">原始属性名</param>
    /// <returns>转换后的蛇形命名字符串</returns>
    public override string ConvertName(string name)
    {
        // 检查输入是否为空或空白
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name)) return string.Empty;

        ReadOnlySpan<char> spanName = name.Trim();
        var stringBuilder = new StringBuilder();
        var addCharacter = true;

        var isPreviousSpace = false;
        var isPreviousSeparator = false;
        var isCurrentSpace = false;
        var isNextLower = false;
        var isNextUpper = false;
        var isNextSpace = false;

        // 遍历字符并根据规则插入分隔符
        for (int position = 0; position < spanName.Length; position++)
        {
            if (position != 0)
            {
                isCurrentSpace = spanName[position] == 32;
                isPreviousSpace = spanName[position - 1] == 32;
                isPreviousSeparator = spanName[position - 1] == 95;

                if (position + 1 != spanName.Length)
                {
                    isNextLower = spanName[position + 1] > 96 && spanName[position + 1] < 123;
                    isNextUpper = spanName[position + 1] > 64 && spanName[position + 1] < 91;
                    isNextSpace = spanName[position + 1] == 32;
                }

                // 处理空格字符的逻辑
                if ((isCurrentSpace) &&
                    ((isPreviousSpace) ||
                    (isPreviousSeparator) ||
                    (isNextUpper) ||
                    (isNextSpace)))
                    addCharacter = false;
                else
                {
                    var isCurrentUpper = spanName[position] > 64 && spanName[position] < 91;
                    var isPreviousLower = spanName[position - 1] > 96 && spanName[position - 1] < 123;
                    var isPreviousNumber = spanName[position - 1] > 47 && spanName[position - 1] < 58;

                    // 在大写字母前插入分隔符的逻辑
                    if ((isCurrentUpper) &&
                    ((isPreviousLower) ||
                    (isPreviousNumber) ||
                    (isNextLower) ||
                    (isNextSpace) ||
                    (isNextLower && !isPreviousSpace)))
                        stringBuilder.Append(_separator);
                    else
                    {
                        if ((isCurrentSpace &&
                            !isPreviousSpace &&
                            !isNextSpace))
                        {
                            stringBuilder.Append(_separator);
                            addCharacter = false;
                        }
                    }
                }
            }

            if (addCharacter)
                stringBuilder.Append(spanName[position]);
            else
                addCharacter = true;
        }

        var result = stringBuilder.ToString().ToLower();
        return result;
    }
}
}
