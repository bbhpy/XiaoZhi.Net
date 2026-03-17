namespace XiaoZhi.Net.Server.Resources
{
/// <summary>
/// 定义ONNX模型的接口，继承自IResource接口
/// </summary>
/// <typeparam name="ModelSetting">模型设置类型</typeparam>
internal interface IOnnxModel : IResource<ModelSetting>
{
    /// <summary>
    /// 获取模型类型
    /// </summary>
    public string ModelType { get; }
    
    /// <summary>
    /// 获取模型名称
    /// </summary>
    public string ModelName { get; }
}
}
