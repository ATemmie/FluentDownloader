using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDownloader.Contracts;

namespace FluentDownloader.Core;

public sealed class SettingsService : ISettingsService
{
    private static readonly string FilePath = Path.Combine(AppSettings.DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded != null) Settings = loaded;
            }
        }
        catch
        {
            // 配置损坏时回退默认值
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch
        {
            // 设置保存失败不致命
        }
    }
}
