using System.Windows.Media;
using FluentDownloader.Contracts;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluentDownloader.App.Services;

/// <summary>主题服务：明暗切换 + Mica 背板 + 跟随系统。</summary>
public static class ThemeService
{
    public static void Apply(AppSettings settings)
    {
        var theme = settings.Theme switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark,
        };

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false);
    }
}

/// <summary>解析 "#RRGGBB" 颜色。</summary>
public static class ColorUtils
{
    public static Color ParseHex(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Color.FromRgb(0x0F, 0x6C, 0xBD);
        }
    }
}
