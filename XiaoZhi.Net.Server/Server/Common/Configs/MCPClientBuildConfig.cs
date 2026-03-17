using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Configs
{
    /// <summary>
    /// MCPClient构建配置
    /// </summary>
    /// <param name="Session"></param>
    /// <param name="ModelSetting"></param>
    internal record MCPClientBuildConfig(Session Session, ModelSetting ModelSetting);
}
