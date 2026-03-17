using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Helpers
{
 /// <summary>
/// 代码执行时间计时器，用于测量代码段的执行时间并记录日志
/// </summary>
internal class CodeTimer : IDisposable
{
    private readonly Stopwatch _stopwatch;
    private readonly string _template;
    private readonly ILogger _logger;

    /// <summary>
    /// 获取已运行的毫秒数
    /// </summary>
    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    /// <summary>
    /// 初始化 CodeTimer 类的新实例
    /// </summary>
    /// <param name="template">日志模板字符串</param>
    /// <param name="logger">日志记录器</param>
    private CodeTimer(string template, ILogger logger)
    {
        this._stopwatch = Stopwatch.StartNew();
        this._template = template;
        this._logger = logger;
    }

    /// <summary>
    /// 创建 CodeTimer 的新实例
    /// </summary>
    /// <param name="template">日志模板字符串</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>CodeTimer 实例</returns>
    public static CodeTimer Create(string template, ILogger logger)
    {
        return new CodeTimer(template, logger);
    }

    /// <summary>
    /// 释放资源并记录执行时间日志
    /// </summary>
    public void Dispose()
    {
        // 记录执行时间到日志
        if (!string.IsNullOrEmpty(this._template))
            this._logger.LogDebug(this._template, this.ElapsedMilliseconds);
        else
            this._logger.LogDebug(Lang.CodeTimer_Dispose_JobFinished, this.ElapsedMilliseconds);
        this._stopwatch.Stop();
    }
}
}
