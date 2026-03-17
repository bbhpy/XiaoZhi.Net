using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    /// <summary>
    /// 表情 枚举，包含了多种常见的情绪状态，每个枚举值都使用Description特性关联了一个对应的表情符号
    /// </summary>
    public enum Emotion
    {
        [Description("😶")]
        Neutral,
        [Description("🙂")]
        Happy,
        [Description("😆")]
        Laughing,
        [Description("😂")]
        Funny,
        [Description("😔")]
        Sad,
        [Description("😠")]
        Angry,
        [Description("😭")]
        Crying,
        [Description("😍")]
        Loving,
        [Description("😳")]
        Embarrassed,
        [Description("😲")]
        Surprised,
        [Description("😱")]
        Shocked,
        [Description("🤔")]
        Thinking,
        [Description("😉")]
        Winking,
        [Description("😎")]
        Cool,
        [Description("😌")]
        Relaxed,
        [Description("🤤")]
        Delicious,
        [Description("😘")]
        Kissy,
        [Description("😏")]
        Confident,
        [Description("😴")]
        Sleepy,
        [Description("😜")]
        Silly,
        [Description("🙄")]
        Confused
    }
}
