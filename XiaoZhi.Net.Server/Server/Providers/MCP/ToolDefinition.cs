using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Server.Providers.MCP
{
    /// <summary>
    /// 工具定义类，存储工具的完整元数据
    /// </summary>
    internal class ToolDefinition
    {
        /// <summary>
        /// 工具名称（如 "HassTurnOn"）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 工具描述（如 "Turns on/opens/presses a device or entity..."）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 输入参数结构（JSON Schema格式）
        /// </summary>
        public JsonObject InputSchema { get; set; }

        /// <summary>
        /// 从JSON对象创建ToolDefinition
        /// </summary>
        public static ToolDefinition FromJson(JsonObject toolObj)
        {
            return new ToolDefinition
            {
                Name = toolObj["name"]?.GetValue<string>(),
                Description = toolObj["description"]?.GetValue<string>(),
                InputSchema = toolObj["inputSchema"]?.AsObject()
            };
        }

        /// <summary>
        /// 转换为JSON对象（用于存储或传输）
        /// </summary>
        public JsonObject ToJson()
        {
            return new JsonObject
            {
                ["name"] = Name,
                ["description"] = Description,
                ["inputSchema"] = InputSchema?.DeepClone()
            };
        }
    }
}
