namespace XiaoZhi.Net.Server.Providers.VAD
{
    /// <summary>
    /// VAD事件回调接口
    /// </summary>
    internal interface IVadEventCallback
    {
        /// <summary>
        /// 检测到语音
        /// </summary>
        /// <param name="audioData"></param>
        void OnVoiceDetected(float[] audioData);
        /// <summary>
        /// 检测到静音
        /// </summary>
        void OnVoiceSilence();
        /// <summary>
        /// 检测到长时间静音
        /// </summary>
        void OnLongTermSilence();
    }
}
