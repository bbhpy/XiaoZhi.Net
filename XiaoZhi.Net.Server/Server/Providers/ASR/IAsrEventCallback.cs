namespace XiaoZhi.Net.Server.Providers.ASR
{
/// <summary>
/// 语音识别事件回调接口，用于处理语音转文本的结果通知
/// </summary>
internal interface IAsrEventCallback
{
    /// <summary>
    /// 当语音转换为文本完成时调用的回调方法
    /// </summary>
    /// <param name="success">转换是否成功，true表示成功，false表示失败</param>
    /// <param name="text">转换后的文本内容，如果转换失败则可能为空或null</param>
    void OnSpeechTextConverted(bool success, string text);
}
}
