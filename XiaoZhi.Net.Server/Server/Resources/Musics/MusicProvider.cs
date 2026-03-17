using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.Musics
{
 /// <summary>
/// 音乐提供者类，负责管理和加载音乐文件
/// </summary>
internal class MusicProvider : BaseResource<MusicProvider, MusicProviderSetting>, IMusics
{
    private MusicProviderSetting? _setting;
    private readonly IDictionary<string, string> _musicFiles;

    /// <summary>
    /// 初始化 MusicProvider 类的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public MusicProvider(ILogger<MusicProvider> logger) : base(logger)
    {
        this._musicFiles = new Dictionary<string, string>();
        this.MusicFiles = this._musicFiles.AsReadOnly();
    }
    
    public override string ResourceName => "MusicProvider";

    public bool HasMusicFiles { get; private set; }
    
    public IReadOnlyDictionary<string, string> MusicFiles { get; private set; }

    /// <summary>
    /// 加载音乐提供者的设置和音乐文件
    /// </summary>
    /// <param name="settings">音乐提供者设置</param>
    /// <returns>如果成功加载则返回true，否则返回false</returns>
    public override bool Load(MusicProviderSetting settings)
    {
        if (string.IsNullOrEmpty(settings.MusicFolderPath))
        {
            this.Logger.LogError(Lang.MusicProvider_Load_PathNotSet);
            return false;
        }

        if (!Directory.Exists(settings.MusicFolderPath))
        {
            this.Logger.LogWarning(Lang.MusicProvider_Load_PathNotExist, settings.MusicFolderPath);
            return true;
        }

        // 获取音乐文件夹中的所有文件
        string[] musicFiles = Directory.GetFiles(settings.MusicFolderPath);

        this.HasMusicFiles = musicFiles.Any();

        foreach (string filePath in musicFiles)
        {
            string fileName = Path.GetFileName(filePath);
            if (!this._musicFiles.ContainsKey(fileName))
            {
                this._musicFiles.Add(fileName, filePath);
            }
        }
        this.MusicFiles = this._musicFiles.AsReadOnly();
        this._setting = settings;

        return true;
    }

    /// <summary>
    /// 更新音乐文件列表
    /// </summary>
    /// <returns>如果成功更新则返回true，否则返回false</returns>
    public bool UpdateMusicFiles()
    {
        if (this._setting is null)
        {
            this.Logger.LogWarning(Lang.MusicProvider_UpdateMusicFiles_SettingsNotInitialized);
            return false;
        }
        
        // 清空现有音乐文件列表
        this._musicFiles.Clear();
        return this.Load(this._setting);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        // 清空音乐文件字典
        this._musicFiles.Clear();
    }

}
}
