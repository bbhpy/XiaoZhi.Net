namespace XiaoZhi.Net.Server.Common.Enums
{
/// <summary>
/// 文本转语音生成结果枚举
/// 定义了TTS（Text-to-Speech）生成操作的各种可能结果状态
/// </summary>
internal enum TtsGenerateResult
{
    /// <summary>
    /// 无结果状态，表示尚未开始或未设置结果
    /// </summary>
    None = 0,
    
    /// <summary>
    /// 操作成功完成
    /// </summary>
    Success,
    
    /// <summary>
    /// 操作失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 操作被中止或取消
    /// </summary>
    Aborted
}
}
