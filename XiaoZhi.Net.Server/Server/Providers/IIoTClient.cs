using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// 物联网客户端
    /// </summary>
    internal interface IIoTClient : IProvider<Session>
    {
        /// <summary>
        /// 处理物联网消息
        /// </summary>
        /// <param name="jsonObject"></param>
        void HandleIoTMessage(JsonObject jsonObject);
        /// <summary>
        /// 执行物联网命令
        /// </summary>
        /// <param name="iotDeviceComponentName"></param>
        /// <param name="functionName"></param>
        /// <param name="parameterInfos"></param>
        /// <param name="arguments"></param>
        /// <returns></returns>
        Task ExecuteIoTCommand(string iotDeviceComponentName, string functionName, IReadOnlyList<KernelParameterMetadata> parameterInfos, KernelArguments arguments);
        /// <summary>
        ///  获取物联网属性状态
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="returnValueType"></param>
        /// <returns></returns>
        object? GetIoTPropertyStatus(string functionName, Type returnValueType);
    }
}
