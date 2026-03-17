using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.OnnxModels
{
  /// <summary>
/// ONNX模型基类，继承自BaseResource
/// </summary>
/// <typeparam name="TLogger">日志记录器类型</typeparam>
internal abstract class BaseOnnxModel<TLogger> : BaseResource<TLogger, ModelSetting>
{
    /// <summary>
    /// 初始化BaseOnnxModel实例
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public BaseOnnxModel(ILogger<TLogger> logger) : base(logger)
    {

    }

    /// <summary>
    /// 获取资源名称
    /// </summary>
    public override string ResourceName => "onnx model";

    /// <summary>
    /// 获取模型类型（抽象属性，需在派生类中实现）
    /// </summary>
    public abstract string ModelType { get; }

    /// <summary>
    /// 获取模型名称（抽象属性，需在派生类中实现）
    /// </summary>
    public abstract string ModelName { get; }

    /// <summary>
    /// 获取模型文件夹路径，组合当前目录、models文件夹、模型类型和转换为kebab-case的模型名称
    /// </summary>
    protected string ModelFileFoler => Path.Combine(Environment.CurrentDirectory, "models", this.ModelType, this.ConvertToKebabCase(this.ModelName));

    /// <summary>
    /// 检查模型文件是否存在
    /// </summary>
    /// <returns>如果模型文件存在返回true，否则返回false</returns>
    protected bool CheckModelExist()
    {
        string modelFilePath = Path.Combine(this.ModelFileFoler, "model.onnx");
        bool exist = File.Exists(modelFilePath);
        if (!exist)
        {
            this.Logger.LogError(Lang.BaseOnnxModel_CheckModelExist_NotFound, modelFilePath);
        }
        return exist;
    }

    /// <summary>
    /// 将输入字符串转换为kebab-case格式
    /// </summary>
    /// <param name="input">需要转换的输入字符串</param>
    /// <returns>转换后的kebab-case格式字符串</returns>
    private string ConvertToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // 使用正则表达式将大写字母前添加连字符并转换为小写
        return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
    }
}
}
