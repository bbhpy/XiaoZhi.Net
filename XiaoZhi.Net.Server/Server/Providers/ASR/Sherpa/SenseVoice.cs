using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
/// <summary>
/// SenseVoice语音识别器类，继承自BaseSherpaAsr并实现IAsr接口
/// </summary>
internal class SenseVoice : BaseSherpaAsr<SenseVoice>, IAsr
{
    /// <summary>
    /// 初始化SenseVoice实例
    /// </summary>
    /// <param name="audioEditor">音频编辑器接口</param>
    /// <param name="logger">日志记录器</param>
    public SenseVoice(IAudioEditor audioEditor, ILogger<SenseVoice> logger) : base(audioEditor, logger)
    {
    }

    /// <summary>
    /// 获取模型名称
    /// </summary>
    public override string ModelName => nameof(SenseVoice);

    /// <summary>
    /// 获取提供者类型
    /// </summary>
    public override string ProviderType => "asr";

    /// <summary>
    /// 构建SenseVoice模型配置
    /// </summary>
    /// <param name="modelSetting">模型设置参数</param>
    /// <returns>构建成功返回true，否则返回false</returns>
    public override bool Build(ModelSetting modelSetting)
    {
        try
        {
            if (!this.CheckModelExist())
            {
                return false;
            }

            // 创建离线识别器配置
            OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();

            // 配置SenseVoice模型路径和逆文本标准化选项
            offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine(ModelFileFoler, "model.onnx");
            offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = modelSetting.Config.GetConfigValueOrDefault("UseInverseTextNormalization", 1);

            this.Build(offlineRecognizerConfig, modelSetting);

            this.Logger.LogInformation(Lang.SenseVoice_Build_Built, this.ProviderType, this.ModelName);

            return true;
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, Lang.SenseVoice_Build_InvalidSettings, this.ProviderType, this.ModelName);
            return false;
        }
    }

}
}
