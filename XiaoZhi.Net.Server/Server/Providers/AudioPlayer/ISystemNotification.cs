using System;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
/// <summary>
/// 系统通知接口，继承自IProvider<AudioSetting>，用于处理音频相关的系统通知功能
/// </summary>
internal interface ISystemNotification : IProvider<AudioSetting>
{
    /// <summary>
    /// 音频数据事件，在有新的音频数据时触发
    /// </summary>
    /// <param name="float[]">音频采样数据数组</param>
    /// <param name="bool">是否为立体声标识</param>
    /// <param name="bool">是否为循环播放标识</param>
    event Action<float[], bool, bool>? OnAudioData;

    /// <summary>
    /// 异步播放绑定码音频
    /// </summary>
    /// <param name="bindCode">要播放的绑定码字符串</param>
    /// <returns>异步任务对象</returns>
    Task PlayBindCodeAsync(string bindCode);

    /// <summary>
    /// 异步播放未找到提示音
    /// </summary>
    /// <returns>异步任务对象</returns>
    Task PlayNotFoundAsync();

    /// <summary>
    /// 异步停止当前音频播放
    /// </summary>
    /// <returns>异步任务对象</returns>
    Task StopAsync();
}
}
