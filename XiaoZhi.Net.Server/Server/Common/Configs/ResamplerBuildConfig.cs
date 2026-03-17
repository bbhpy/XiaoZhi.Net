namespace XiaoZhi.Net.Server.Common.Configs
{/// <summary>
/// 表示重采样器构建配置的数据记录
/// </summary>
/// <param name="Channels">音频通道数</param>
/// <param name="InSampleRate">输入采样率</param>
/// <param name="OutSampleRate">输出采样率</param>
internal record ResamplerBuildConfig(int Channels, int InSampleRate, int OutSampleRate);
}
