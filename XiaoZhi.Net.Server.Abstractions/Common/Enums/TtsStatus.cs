using System.ComponentModel;

namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    /// <summary>
    /// 文本转语音状态
    /// </summary>
    public enum TtsStatus
    {
        /// <summary>
        /// 开始
        /// </summary>
        [Description("start")]
        Start,
        /// <summary>
        ///  停止
        /// </summary>
        [Description("stop")]
        Stop,
        /// <summary>
        ///  句子开始
        /// </summary>
        [Description("sentence_start")]
        SentenceStart,
        /// <summary>
        ///  句子结束
        /// </summary>
        [Description("sentence_end")]
        SentenceEnd
    }
}
