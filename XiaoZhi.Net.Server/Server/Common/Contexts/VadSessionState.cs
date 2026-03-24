using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    /// <summary>
    /// VAD（语音活动检测）会话状态类，用于管理语音检测跟踪、滑动窗口分析和会话状态持久化
    /// </summary>
    internal class VadSessionState
    {
        private const int DEFAULT_VOICE_WINDOW_SIZE = 8;

        /// <summary>
        /// 使用默认语音窗口大小初始化 VadSessionState 类的新实例
        /// </summary>
        public VadSessionState() : this(DEFAULT_VOICE_WINDOW_SIZE)
        {
        }

        /// <summary>
        /// 使用指定的语音窗口大小初始化 VadSessionState 类的新实例
        /// </summary>
        /// <param name="voiceWindowSize">语音窗口大小</param>
        public VadSessionState(int voiceWindowSize)
        {
            VoiceWindow = new Queue<bool>(voiceWindowSize);
            _voiceWindowSize = voiceWindowSize;
        }

        private readonly int _voiceWindowSize;

        /// <summary>
        /// 音频缓冲区中下一个要分析的样本索引
        /// 这使得VAD可以在不从缓冲区中删除数据的情况下进行分析
        /// </summary>
        public int AnalyzedIndex { get; set; }

        /// <summary>
        /// 最近一次检测到语音的时间（Unix时间戳，单位毫秒）
        /// </summary>
        public long HaveVoiceLatestTime { get; set; }

        /// <summary>
        /// 指示当前会话中是否已检测到语音
        /// </summary>
        public bool HaveVoice { get; set; }

        /// <summary>
        /// 指示上一帧是否检测到语音（用于滞后处理）
        /// </summary>
        public bool LastIsVoice { get; set; }

        /// <summary>
        /// 指示语音检测到后是否已停止
        /// </summary>
        public bool VoiceStop { get; set; }
        /// <summary>
        /// 静音开始的时间戳（Unix毫秒）
        /// </summary>
        public long SilenceStartTime { get; set; }
        /// <summary>
        /// 是否正在等待静音阈值
        /// </summary>
        public bool IsWaitingForSilence { get; set; }
        /// <summary>
        /// 用于跨多个帧跟踪语音活动的滑动窗口
        /// </summary>
        public Queue<bool> VoiceWindow { get; private set; }
        /// <summary>
        /// 连续静音帧数
        /// </summary>
        public int ConsecutiveSilenceFrames { get; set; }

        /// <summary>
        /// 将语音检测结果添加到滑动窗口中
        /// </summary>
        /// <param name="isVoice">是否为语音</param>
        public void AddToVoiceWindow(bool isVoice)
        {
            if (VoiceWindow.Count >= _voiceWindowSize)
            {
                VoiceWindow.Dequeue();
            }
            VoiceWindow.Enqueue(isVoice);
        }

        /// <summary>
        /// 计算语音窗口中有多少个true值
        /// </summary>
        /// <returns>语音窗口中的true值数量</returns>
        public int CountVoiceInWindow()
        {
            int count = 0;
            foreach (bool isVoice in VoiceWindow)
            {
                if (isVoice) count++;
            }
            return count;
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        public void Reset()
        {
            HaveVoice = false;
            HaveVoiceLatestTime = 0;
            AnalyzedIndex = 0;
            LastIsVoice = false;
            VoiceStop = false;
            VoiceWindow.Clear();
            SilenceStartTime = 0;
            IsWaitingForSilence = false;
            ConsecutiveSilenceFrames = 0;
        }
    }
}
