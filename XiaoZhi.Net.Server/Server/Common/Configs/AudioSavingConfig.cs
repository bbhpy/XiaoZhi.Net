using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Server.Common.Configs
{
    /// <summary>
    /// 音频保存配置记录类，用于定义音频文件的保存设置
    /// </summary>
    /// <param name="SaveFile">是否保存音频文件，默认为false</param>
    /// <param name="SavePath">音频文件保存路径，默认为"./data/audio-cache"</param>
    /// <param name="Format">音频文件格式，默认为"wav"</param>
    /// <param name="SampleRate">采样率，默认为16000Hz</param>
    /// <param name="Channels">声道数，默认为1（单声道）</param>
    /// <param name="BitRate">比特率，默认为128000bps</param>
    /// <returns>AudioSavingConfig实例</returns>
    [method: JsonConstructor]
    internal record AudioSavingConfig(
        [property: JsonPropertyName("SaveFile")] bool SaveFile = false,
        [property: JsonPropertyName("SavePath")] string SavePath = "./data/audio-cache",
        [property: JsonPropertyName("Format")] string Format = "wav",
        [property: JsonPropertyName("SampleRate")] int SampleRate = 16000,
        [property: JsonPropertyName("Channels")] int Channels = 1,
        [property: JsonPropertyName("BitRate")] int BitRate = 128000)
    {
        /// <summary>
        /// 使用指定的保存文件选项创建音频保存配置的构造函数
        /// </summary>
        /// <param name="SaveFile">是否保存音频文件</param>
        public AudioSavingConfig(bool SaveFile)
            : this(SaveFile, "./data/audio-cache", "wav", 16000, 1, 128000)
        {
        }
    }
}