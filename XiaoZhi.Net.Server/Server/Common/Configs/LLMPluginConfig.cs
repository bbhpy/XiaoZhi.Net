using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Server.Common.Configs
{
    /// <summary>
/// 表示LLM插件配置的基础记录类型
/// </summary>
/// <param name="Kernel">用于插件的核心内核实例</param>
internal record LLMPluginConfig(Kernel Kernel);

/// <summary>
/// 表示带有特定设置类型的LLM插件配置的泛型记录类型
/// </summary>
/// <typeparam name="TPluginSetting">插件设置的具体类型</typeparam>
/// <param name="Kernel">用于插件的核心内核实例</param>
/// <param name="setting">插件的特定设置实例</param>
internal record LLMPluginConfig<TPluginSetting>(Kernel Kernel, TPluginSetting setting) : LLMPluginConfig(Kernel);
}
