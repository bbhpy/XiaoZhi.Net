using System.IO;

namespace XiaoZhi.Net.Server.Resources
{
/// <summary>
/// 设备绑定接口，继承自IResource接口，用于处理设备绑定相关的音频流操作
/// </summary>
internal interface IDeviceBinding : IResource<DeviceBindSetting>
{
    /// <summary>
    /// 获取设备未找到时的音频流
    /// </summary>
    /// <returns>设备未找到音频流，如果不存在则返回null</returns>
    Stream? GetDeviceNotFoundAudioStream();

    /// <summary>
    /// 根据绑定码获取对应的设备绑定音频流
    /// </summary>
    /// <param name="bindCode">设备绑定码</param>
    /// <returns>对应绑定码的设备绑定音频流，如果不存在则返回null</returns>
    Stream? GetDeviceBindCodeAudioStream(string bindCode);
}
}
