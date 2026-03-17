using System;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Common.Models
{
/// <summary>
/// 表示IoT设备属性的类，用于描述IoT组件中的属性信息
/// </summary>
internal class IoTProperty
{
    /// <summary>
    /// 初始化IoTProperty类的新实例
    /// </summary>
    /// <param name="name">属性名称</param>
    /// <param name="iotComponentName">IoT组件名称</param>
    /// <param name="type">属性类型描述</param>
    public IoTProperty(string name, string iotComponentName, string type)
    {
        this.Name = name;
        this.IoTComponentName = iotComponentName;
        this.TypeDescription = type;
        // 根据类型描述获取对应的IoT类型
        this.Type = IoTTypeMappingHelper.GetIoTType(type);
        // 根据类型描述获取该类型的默认值
        this.StatusValue = IoTTypeMappingHelper.GetDefaultValue(type);
    }

    /// <summary>
    /// 获取属性名称
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// 获取IoT组件名称
    /// </summary>
    public string IoTComponentName { get; }
    
    /// <summary>
    /// 获取属性的实际Type类型
    /// </summary>
    public Type Type { get; }
    
    /// <summary>
    /// 获取属性类型描述
    /// </summary>
    public string TypeDescription { get; }
    
    /// <summary>
    /// 获取或设置状态值
    /// </summary>
    public object? StatusValue { get; set; }
}
}
