using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    /// <summary>
    /// 输出段落
    /// </summary>
    internal class OutSegment
    {
        private string _content;
        /// <summary>
        /// 段落内容
        /// </summary>
        public OutSegment()
        {
            this._content = string.Empty;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
            this.Emotion = Emotion.Neutral;
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public string Content => _content;

        /// <summary>
        /// 是否为第一段
        /// </summary>
        public bool IsFirstSegment { get; set; }

        /// <summary>
        /// 是否为最后一段
        /// </summary>
        public bool IsLastSegment { get; set; }

        /// <summary>
        /// 当前段落的情绪
        /// </summary>
        public Emotion Emotion { get; set; }

        /// <summary>
        /// 段落Id
        /// </summary>
        public string? ParagraphId { get; set; }

        /// <summary>
        /// 句子Id
        /// </summary>
        public string? SentenceId { get; set; }

/// <summary>
/// 初始化对象，设置内容、情感和其他可选标识符
/// </summary>
/// <param name="content">要初始化的内容字符串</param>
/// <param name="emotion">与内容相关的情感枚举值</param>
/// <param name="paragraphId">段落标识符（可选）</param>
/// <param name="sentenceId">句子标识符（可选）</param>
public void Initialize(string content, Emotion emotion, string? paragraphId = null, string? sentenceId = null)

        {
            this._content = content;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
            this.Emotion = emotion;
            this.ParagraphId = paragraphId;
            this.SentenceId = sentenceId;
        }

/// <summary>
/// 初始化对象，设置内容、段落位置标记、情感和其他可选标识符
/// </summary>
/// <param name="content">要初始化的内容字符串</param>
/// <param name="isFirst">指示是否为第一个片段的布尔值</param>
/// <param name="isLast">指示是否为最后一个片段的布尔值</param>
/// <param name="emotion">与内容相关的情感枚举值</param>
/// <param name="paragraphId">段落标识符（可选）</param>
/// <param name="sentenceId">句子标识符（可选）</param>
        public void Initialize(string content, bool isFirst, bool isLast, Emotion emotion, string? paragraphId = null, string? sentenceId = null)
        {
            this._content = content;
            this.IsFirstSegment = isFirst;
            this.IsLastSegment = isLast;
            this.Emotion = emotion;
            this.ParagraphId = paragraphId;
            this.SentenceId = sentenceId;
        }

/// <summary>
/// 重置对象的所有属性到默认状态
/// </summary>
        public virtual void Reset()
        {
            // 重置所有属性到初始状态
            this._content = string.Empty;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
            this.Emotion = Emotion.Neutral;
            this.SentenceId = null;
            this.ParagraphId = null;
        }

    }
}
