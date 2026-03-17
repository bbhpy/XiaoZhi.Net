using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System;
using System.Buffers;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Handlers
{
    /// <summary>
    /// 消息处理  
    /// </summary>
    internal class MessageDispatch
    {

        private static readonly Serilog.ILogger _logger  = new LoggerConfiguration()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:w3}] {Message:lj}{NewLine}{Exception}",
                theme: Serilog.Sinks.SystemConsole.Themes.SystemConsoleTheme.Literate) // 使用Literate主题
            .CreateLogger();
        public static async ValueTask DispatchAsync(WebSocketSession appSession, WebSocketPackage package)
        {
            if (appSession is null || package is null)
            {
                throw new ArgumentNullException(Lang.MessageDispatch_DispatchAsync_ArgumentNull);
            }
            if (appSession is SocketSession session && session.XiaoZhiSession is not null)
            {
                switch (package.OpCode)
                {
                    case OpCode.Text:
                        JsonNode? jsonObject = JsonNode.Parse(package.Message);
                        string? type = jsonObject?["type"]?.GetValue<string>()?.ToLower();
                        if (jsonObject is JsonObject jsonObj && !string.IsNullOrEmpty(type) && type == "hello")
                        {
                            _logger.Information("客户端{SessionId}发送来Hello消息，Json为：{json}", session.SessionId, jsonObj);
                            await session.XiaoZhiSession.HandlerPipeline.HandleHelloMessage(jsonObj);
                        }
                        else
                        {
                            _logger.Information("客户端{SessionId}发送来文本消息：{json}", session.SessionId, package.Message);
                           session.XiaoZhiSession.HandlerPipeline.HandleTextMessage(package.Message);
                        }
                        break;
                    case OpCode.Binary:
                        //_logger.Information("客户端{SessionId}发送来消息十六进制：{json}", session.SessionId, BitConverter.ToString(package.Data.ToArray()).Replace("-", ""));
                        await session.XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(package.Data.ToArray());
                        break;
                    //case OpCode.Ping:
                    //    break;
                    //case OpCode.Pong:
                    //    break;
                }
            }
        }
    }
}
