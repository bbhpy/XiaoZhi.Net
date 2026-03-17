using SherpaOnnx;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    /// <summary>
    /// 音频数据包类，用于管理和处理音频数据的缓冲、存储和检索
    /// </summary>
    internal class AudioPacket
    {
        private bool _released;
        private const int DEFAULT_BUFFER_CAPACITY = 960 * 100;
        private readonly CircularBuffer _audioBuffer;

        /// <summary>
        /// 初始化新的音频数据包实例
        /// </summary>
        public AudioPacket()
        {
            this._audioBuffer = new CircularBuffer(DEFAULT_BUFFER_CAPACITY);
        }

        /// <summary>
        /// 获取或设置最后一次检测到语音的时间戳
        /// </summary>
        public long LastHaveVoiceTime { get; set; }

        /// <summary>
        /// 获取或设置最新检测到语音的时间戳
        /// </summary>
        public long HaveVoiceLatestTime { get; set; }

        /// <summary>
        /// 获取或设置是否检测到语音的标志
        /// </summary>
        public bool HaveVoice { get; set; }

        /// <summary>
        /// 获取或设置语音停止状态的标志
        /// </summary>
        public bool VoiceStop { get; set; }

        /// <summary>
        /// 获取音频缓冲区的头部位置
        /// </summary>
        public int BufferHead => this._audioBuffer.Head;

        /// <summary>
        /// 获取音频缓冲区的大小
        /// </summary>
        public int BufferSize => this._audioBuffer.Size;

        /// <summary>
        /// 将音频数据推入缓冲区
        /// </summary>
        /// <param name="audioData">要推入的音频数据数组</param>
        public void PushAudio(float[] audioData)
        {
            if (!this._released && audioData.Length > 0)
            {
                this._audioBuffer.Push(audioData);
            }
        }

        /// <summary>
        /// 从缓冲区获取指定起始位置和帧大小的音频数据
        /// </summary>
        /// <param name="startIndex">开始索引位置</param>
        /// <param name="frameSize">要获取的帧大小</param>
        /// <returns>音频数据数组，如果条件不符合则返回空数组</returns>
        public float[] GetFrames(int startIndex, int frameSize)
        {
            if (this._released || this._audioBuffer.Size < startIndex + frameSize)
            {
                return [];
            }
            return this._audioBuffer.Get(startIndex, frameSize);
        }

        /// <summary>
        /// 从缓冲区弹出指定数量的帧
        /// </summary>
        /// <param name="count">要弹出的帧数量</param>
        public void PopFrames(int count)
        {
            if (!this._released && count > 0 && this._audioBuffer.Size >= count)
            {
                this._audioBuffer.Pop(count);
            }
        }

        /// <summary>
        /// 获取缓冲区中的所有音频数据
        /// </summary>
        /// <returns>包含所有音频数据的数组，如果条件不符合则返回空数组</returns>
        public float[] GetAllAudio()
        {
            if (this._released || this._audioBuffer.Size == 0)
            {
                return [];
            }
            return this._audioBuffer.Get(this._audioBuffer.Head, this._audioBuffer.Size);
        }

        /// <summary>
        /// 重置音频缓冲区
        /// </summary>
        public void ResetAudioBuffer()
        {
            if (!this._released)
            {
                this._audioBuffer.Reset();
            }
        }

        /// <summary>
        /// 重置音频数据包的所有状态和缓冲区
        /// </summary>
        public void Reset()
        {
            this.VoiceStop = false;
            this.HaveVoice = false;
            this.HaveVoiceLatestTime = 0;
            this.LastHaveVoiceTime = 0;
            this.ResetAudioBuffer();
        }

        /// <summary>
        /// 释放音频数据包资源
        /// </summary>
        public void Release()
        {
            this._released = true;
            this._audioBuffer.Dispose();
        }

        /// <summary>
        /// 裁剪旧的音频数据，保留最新的指定帧数
        /// </summary>
        /// <param name="keepFrames">要保留的帧数，默认为1024</param>
        public void TrimOldAudio(int keepFrames = 1024)
        {
            if (!this._released && this._audioBuffer.Size > keepFrames)
            {
                this._audioBuffer.Pop(this._audioBuffer.Size - keepFrames);
            }
        }
    }
}
