using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
/// 提供者基类，用于定义通用提供者的抽象实现
/// </summary>
/// <typeparam name="TLogger">日志记录器类型</typeparam>
/// <typeparam name="TSettings">设置类型，必须是引用类型</typeparam>
internal abstract class BaseProvider<TLogger, TSettings> : IProvider<TSettings> where TSettings : class
{
    /// <summary>
    /// 初始化BaseProvider的新实例
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public BaseProvider(ILogger<TLogger> logger)
    {
        this.Logger = logger;
    }

    /// <summary>
    /// 获取提供者类型
    /// </summary>
    public abstract string ProviderType { get; }
    
    /// <summary>
    /// 获取模型名称
    /// </summary>
    public abstract string ModelName { get; }
    
    /// <summary>
    /// 检查是否为Sherpa模型
    /// </summary>
    public bool IsSherpaModel => this.CheckIsSherpaModel();
    
    /// <summary>
    /// 获取模型文件夹路径
    /// </summary>
    protected string ModelFileFoler => Path.Combine(Environment.CurrentDirectory, "models", this.ProviderType, this.ConvertToKebabCase(this.ModelName));

    /// <summary>
    /// 获取日志记录器
    /// </summary>
    protected ILogger<TLogger> Logger { get; }

    /// <summary>
    /// 获取或设置会话ID
    /// </summary>
    protected string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 获取或设置设备ID
    /// </summary>
    protected string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// 构建提供者实例
    /// </summary>
    /// <param name="settings">提供者设置</param>
    /// <returns>构建成功返回true，否则返回false</returns>
    public abstract bool Build(TSettings settings);
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public abstract void Dispose();

    /// <summary>
    /// 注册设备
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sessionId">会话ID</param>
    public virtual void RegisterDevice(string deviceId, string sessionId)
    {
        this.DeviceId = deviceId;
        this.SessionId = sessionId;
        this.Logger.LogInformation(Lang.BaseProvider_RegisterDevice_Registered, this.DeviceId, this.SessionId, this.ProviderType);
    }

    /// <summary>
    /// 注销设备
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sessionId">会话ID</param>
    public virtual void UnregisterDevice(string deviceId, string sessionId)
    {
        this.Logger.LogInformation(Lang.BaseProvider_UnregisterDevice_Unregistered, this.DeviceId, this.ProviderType, this.SessionId);
        this.DeviceId = string.Empty;
        this.SessionId = string.Empty;
    }

    /// <summary>
    /// 检查设备是否已注册
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>设备已注册返回true，否则返回false</returns>
    public virtual bool CheckDeviceRegistered(string deviceId, string sessionId)
    {
        if (string.IsNullOrEmpty(this.DeviceId) || string.IsNullOrEmpty(this.SessionId))
        {
            this.Logger.LogError(Lang.BaseProvider_CheckDeviceRegistered_NotRegistered, string.IsNullOrEmpty(this.DeviceId) ? "unkonwn" : this.DeviceId, string.IsNullOrEmpty(this.SessionId) ? "unkonwn" : this.SessionId, this.ProviderType);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 检查模型文件是否存在
    /// </summary>
    /// <returns>模型存在返回true，否则返回false</returns>
    protected bool CheckModelExist()
    {
        string modelFilePath = Path.Combine(this.ModelFileFoler, "model.onnx");
        bool exist = File.Exists(modelFilePath);
        if (!exist)
        {
            this.Logger.LogError(Lang.BaseProvider_CheckModelExist_NotFound, modelFilePath);
        }
        return exist;
    }

    /// <summary>
    /// 生成唯一标识符
    /// </summary>
    /// <returns>新生成的GUID字符串</returns>
    protected virtual string GenerateId()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 替换MAC地址分隔符
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="newDelimiter">新的分隔符，默认为空字符串</param>
    /// <returns>处理后的设备ID</returns>
    protected string ReplaceMacDelimiters(string deviceId, string newDelimiter = "")
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException(Lang.BaseProvider_ReplaceMacDelimiters_DeviceIdNull, nameof(deviceId));
        }

        return Regex.Replace(deviceId, @"[^a-fA-F0-9]", newDelimiter);
    }


    /// <summary>
    /// 检查当前模型是否为Sherpa模型
    /// </summary>
    /// <returns>如果是Sherpa模型返回true，否则返回false</returns>
    private bool CheckIsSherpaModel()
    {
        switch (this.ProviderType.ToLower())
        {
            case "vad" when SherpaModels.VadModels.Contains(this.ModelName):
            case "asr" when SherpaModels.AsrModels.Contains(this.ModelName):
            case "tts" when SherpaModels.TtsModels.Contains(this.ModelName):
                return true;
        }
        return false;
    }

    /// <summary>
    /// 将驼峰命名转换为短横线命名
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <returns>转换后的短横线命名字符串</returns>
    private string ConvertToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
    }
}
}

