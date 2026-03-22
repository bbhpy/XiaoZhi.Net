using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal class Silero : BaseSherpaVad<Silero>, IVad
    {
        public Silero(ILogger<Silero> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(Silero);
        public override bool Build(ModelSetting modelSetting)
        {
            /// <summary>
            /// 构建VAD（Voice Activity Detection）模型
            /// </summary>
            /// <returns>如果模型构建成功则返回true，否则返回false</returns>
            try
            {
                // 检查模型是否存在
                if (!this.CheckModelExist())
                {
                    return false;
                }

                // 创建VAD模型配置对象并设置相关参数
                VadModelConfig vadModelConfig = new VadModelConfig();
                vadModelConfig.SileroVad.Model = Path.Combine(this.ModelFileFoler, "model.onnx");
                // 语音阈值，范围为0到1，默认值为0.5
                vadModelConfig.SileroVad.Threshold = modelSetting.Config.GetConfigValueOrDefault("Threshold", 0.5f);
                // 设置静音阈值和语音持续时间的相关参数   静音阈值秒
                vadModelConfig.SileroVad.MinSilenceDuration = modelSetting.Config.GetConfigValueOrDefault("SilenceThresholdSecond", 1.5f);
                // 最短语音持续时间（秒） 
                vadModelConfig.SileroVad.MinSpeechDuration = modelSetting.Config.GetConfigValueOrDefault("MinSpeechDurationSecond", 0.7f);
                //最大语音持续时间秒 
                vadModelConfig.SileroVad.MaxSpeechDuration = modelSetting.Config.GetConfigValueOrDefault("MaxSpeechDurationSecond", 60.0f);

                // 执行模型构建操作
                if (this.Build(vadModelConfig, modelSetting))
                {
                    this.Logger.LogInformation(Lang.Silero_Build_Built, this.ProviderType, this.ModelName);
                    return true;
                }
                else
                    return false;
            }
            catch (Exception ex)
            {
                // 记录模型构建过程中的异常信息
                this.Logger.LogError(ex, Lang.Silero_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }

        }


    }
}
