using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentDownloader.Contracts;
using FluentDownloader.Core;
using Microsoft.Web.WebView2.Core;

namespace FluentDownloader.Module.Browser;

public partial class BrowserPage
{
    private readonly ITaskManager _taskManager;
    private readonly ISettingsService _settingsService;
    private ResourceSniffer? _sniffer;
    private bool _pickerActive;
    private bool _coreInitialized;

    public BrowserPage(ITaskManager taskManager, ISettingsService settingsService)
    {
        _taskManager = taskManager;
        _settingsService = settingsService;
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        if (_coreInitialized) return;
        _coreInitialized = true;

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(AppSettings.DataDirectory, "WebView2"));
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;

            _sniffer = new ResourceSniffer(core, _taskManager, _settingsService);
            await _sniffer.StartAsync();
            ResourceList.ItemsSource = _sniffer.Resources;

            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            core.WebMessageReceived += OnWebMessageReceived;

            NavigateTo("https://www.bilibili.com");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"WebView2 初始化失败：{ex.Message}（请安装 Edge WebView2 运行时）";
        }
    }

    // ---------- 导航 ----------

    private void NavigateTo(string url)
    {
        if (_sniffer != null) _sniffer.Clear();
        if (WebView.CoreWebView2 != null)
        {
            WebView.CoreWebView2.Navigate(url);
            AddressBox.Text = url;
        }
    }

    private void OnAddressKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        NavigateTo(NormalizeUrl(AddressBox.Text));
    }

    private static string NormalizeUrl(string input)
    {
        var text = input.Trim();
        if (text.Length == 0) return text;
        if (!text.Contains("://")) text = "https://" + text;
        return text;
    }

    private void OnBack(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.GoBack();

    private void OnForward(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.GoForward();

    private void OnReload(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Reload();

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // 在当前窗口打开新页面（单标签 MVP）
        e.Handled = true;
        NavigateTo(e.Uri);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AddressBox.Text = WebView.CoreWebView2?.Source ?? AddressBox.Text;
        if (_pickerActive && WebView.CoreWebView2 != null)
        {
            try { await WebView.CoreWebView2.ExecuteScriptAsync(ElementPickerScript.Source); }
            catch { /* 注入失败忽略 */ }
        }
    }

    // ---------- 嗅探 ----------

    private void OnClearResources(object sender, RoutedEventArgs e) => _sniffer?.Clear();

    private void OnDownloadResource(object sender, RoutedEventArgs e)
    {
        if (_sniffer == null) return;
        if ((sender as FrameworkElement)?.Tag is not ResourceInfo resource) return;

        try
        {
            var task = _taskManager.Enqueue(resource.ToRequest(
                _settingsService.Settings.CookiesFilePath,
                _settingsService.Settings.DownloadDirectory));
            StatusText.Text = $"已加入下载：{task.Title}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    // ---------- 点选模式 ----------

    private async void OnPickerToggle(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 == null) return;
        try
        {
            var result = await WebView.CoreWebView2.ExecuteScriptAsync(ElementPickerScript.Source);
            _pickerActive = result.Trim() == "true";
            PickerHint.Visibility = _pickerActive ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"点选模式启动失败：{ex.Message}";
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.GetProperty("type").GetString() != "fdl-pick") return;

            var url = doc.RootElement.GetProperty("url").GetString() ?? "";
            var tag = doc.RootElement.TryGetProperty("tag", out var t) ? t.GetString() ?? "" : "";

            if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "该元素指向 blob 加密流，无法直接下载 —— 请查看右侧嗅探列表里的 m3u8/视频资源";
                return;
            }

            var kind = url.Contains(".m3u8") || url.Contains(".mpd") ? ResourceKind.Playlist : ResourceKind.File;
            var resource = new ResourceInfo
            {
                Url = url,
                FileName = GuessName(url),
                Kind = kind,
                PageUrl = WebView.CoreWebView2?.Source,
            };

            var task = _taskManager.Enqueue(resource.ToRequest(
                _settingsService.Settings.CookiesFilePath,
                _settingsService.Settings.DownloadDirectory));
            StatusText.Text = $"已加入下载：{task.Title}（元素 <{tag}>）";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"点选处理失败：{ex.Message}";
        }
    }

    private static string GuessName(string url)
    {
        try
        {
            var last = Uri.UnescapeDataString(new Uri(url).Segments[^1]).TrimEnd('/');
            return last.Length > 0 ? last : "download.bin";
        }
        catch
        {
            return "download.bin";
        }
    }

    // ---------- Cookies 同步 ----------

    private async void OnExportCookies(object sender, RoutedEventArgs e)
    {
        var core = WebView.CoreWebView2;
        if (core?.Source == null) return;

        try
        {
            var origin = new Uri(core.Source).GetLeftPart(UriPartial.Authority);
            var cookies = await core.CookieManager.GetCookiesAsync(origin);

            var sb = new StringBuilder("# Netscape HTTP Cookie File\n");
            foreach (var cookie in cookies)
            {
                var domain = cookie.Domain.StartsWith('.') ? cookie.Domain : cookie.Domain;
                var expires = cookie.IsSession ? 0 : new DateTimeOffset(cookie.Expires).ToUnixTimeSeconds();
                sb.Append(domain).Append('\t')
                  .Append(domain.StartsWith('.') ? "TRUE" : "FALSE").Append('\t')
                  .Append(cookie.Path).Append('\t')
                  .Append(cookie.IsSecure ? "TRUE" : "FALSE").Append('\t')
                  .Append(expires).Append('\t')
                  .Append(cookie.Name).Append('\t')
                  .Append(cookie.Value).Append('\n');
            }

            if (cookies.Count == 0)
            {
                StatusText.Text = "当前站点没有可导出的 cookies（先登录再同步）";
                return;
            }

            var path = Path.Combine(AppSettings.DataDirectory, "cookies.txt");
            File.WriteAllText(path, sb.ToString());
            _settingsService.Settings.CookiesFilePath = path;
            _settingsService.Save();
            StatusText.Text = $"已导出 {cookies.Count} 条 cookies：后续视频/音乐/HTTP 下载将自动携带登录态";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cookies 导出失败：{ex.Message}";
        }
    }
}

/// <summary>资源大小 → 文本。</summary>
public sealed class BytesTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long b && b > 0 ? FormatUtils.Bytes(b) : "--";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
