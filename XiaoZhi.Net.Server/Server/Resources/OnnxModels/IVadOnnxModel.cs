using XiaoZhi.Net.Server.Resources.OnnxModels.VAD.Models;

namespace XiaoZhi.Net.Server.Resources.OnnxModels
{
/// <summary>
/// VAD (Voice Activity Detection) ONNX模型接口，用于语音活动检测
/// 继承自IOnnxModel接口，提供基于ONNX模型的语音活动检测功能
/// </summary>
internal interface IVadOnnxModel : IOnnxModel
{
    /// <summary>
    /// 执行语音活动检测推理
    /// </summary>
    /// <param name="audioSamples">音频采样数据数组，包含待检测的音频样本</param>
    /// <param name="sampleRate">音频采样率，指定输入音频的采样频率</param>
    /// <param name="modelState">Silero模型状态对象，用于维护模型的内部状态</param>
    /// <returns>返回语音活动检测的结果，浮点数值表示语音活动的概率或置信度</returns>
    float Infer(float[] audioSamples, int sampleRate, SileroModelState modelState);
}
}
