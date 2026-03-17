using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Protocol
{
    /// <summary>
    /// 业务层发送接口 
    /// </summary>
    internal interface IBizSendOutter : ISocketSendOutter
    {
        /// <summary>
        ///  会话ID
        /// </summary>
        string SessionId { get; }
        /// <summary>
        ///  获取会话对象 
        /// </summary>
        /// <returns></returns>
        Session GetSession();
        /// <summary>
        ///  发送语音合成消息 
        /// </summary>
        /// <param name="state"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        Task SendTtsMessageAsync(TtsStatus state, string? text = null);
        /// <summary>
        ///  发送即时语音转文字消息 
        /// </summary>
        /// <param name="sttText"></param>
        /// <returns></returns>
        Task SendSttMessageAsync(string sttText);
        /// <summary>
        ///  发送大型语言模型消息
        /// </summary>
        /// <param name="emotion"></param>
        /// <returns></returns>
        Task SendLlmMessageAsync(Emotion emotion);
        /// <summary>
        ///  发送中止消息
        /// </summary>
        /// <returns></returns>
        Task SendAbortMessageAsync();
        /// <summary>
        ///  关闭会话
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        Task CloseSessionAsync(string reason = "");
    }
}
