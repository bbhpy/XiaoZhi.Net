using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    /// <summary>
    /// 音频类型
    /// </summary>
    [Flags]
    public enum AudioType
    {
        /// <summary>
        /// 无
        /// </summary>
        [Description("None")]
        None = 0,
        /// <summary>
        /// 音乐
        /// </summary>
        [Description("Music")]
        Music = 2,
        /// <summary>
        /// 文本转语音
        /// </summary>
        [Description("TTS")]
        TTS = 4,
        /// <summary>
        /// 系统通知
        /// </summary>
        [Description("System Notification")]
        SystemNotification = 8,
        /// <summary>
        /// 其他
        /// </summary>
        [Description("Other")]
        Other = 99
    }
}
