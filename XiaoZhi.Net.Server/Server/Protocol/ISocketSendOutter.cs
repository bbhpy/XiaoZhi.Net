using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Protocol
{
    /// <summary>
    /// 封装Socket发送
    /// </summary>
    internal interface ISocketSendOutter
    {
        /// <summary>
        /// 发送json
        /// </summary>
        /// <param name="json"></param>
        /// <param name="topic">mqtt用</param>
        /// <returns></returns>
        Task SendAsync(string json,string topic);
        /// <summary>
        ///  发送字节
        /// </summary>
        /// <param name="bytePacket"></param>
        /// <returns></returns>
        Task SendAsync(byte[] bytePacket);
    }
}
