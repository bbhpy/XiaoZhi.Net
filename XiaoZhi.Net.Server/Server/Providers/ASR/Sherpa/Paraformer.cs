using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
/// <summary>
/// Paraformer语音识别器类，继承自BaseSherpaAsr并实现IAsr接口
/// </summary>
internal class Paraformer : BaseSherpaAsr<Paraformer>, IAsr
{
    /// <summary>
    /// 初始化Paraformer实例
    /// </summary>
    /// <param name="audioEditor">音频编辑器接口</param>
    /// <param name="logger">日志记录器</param>
    public Paraformer(IAudioEditor audioEditor, ILogger<Paraformer> logger) : base(audioEditor, logger)
    {
    }

    /// <summary>
    /// 获取模型名称
    /// </summary>
    public override string ModelName => nameof(Paraformer);

    /// <summary>
    /// 构建Paraformer模型
    /// </summary>
    /// <param name="modelSetting">模型设置参数</param>
    /// <returns>构建成功返回true，否则返回false</returns>
    public override bool Build(ModelSetting modelSetting)
    {
        try
        {
            // 检查模型文件是否存在
            if (!this.CheckModelExist())
            {
                return false;
            }
            
            // 创建离线识别器配置
            OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
            offlineRecognizerConfig.ModelConfig.Paraformer.Model = Path.Combine(ModelFileFoler, "model.onnx");

            // 执行模型构建
            this.Build(offlineRecognizerConfig, modelSetting);

            // 记录构建成功的日志
            this.Logger.LogInformation(Lang.Paraformer_Build_Built, this.ProviderType, this.ModelName);
            return true;
        }
        catch (Exception ex)
        {
            // 记录构建失败的日志
            this.Logger.LogError(ex, Lang.Paraformer_Build_InvalidSettings, this.ProviderType, this.ModelName);
            return false;
        }
    }
}
}
