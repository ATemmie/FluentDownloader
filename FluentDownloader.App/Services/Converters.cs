using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FluentDownloader.Contracts;
using FluentDownloader.Core;

namespace FluentDownloader.App.Services;

/// <summary>字节 → 人类可读大小。</summary>
public sealed class BytesTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long b && b > 0 ? FormatUtils.Bytes(b) : "--";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>速度 → "x MB/s"。</summary>
public sealed class SpeedTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => FormatUtils.Speed((long)(value ?? 0L));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>剩余时间估算（进度 + 速度 → 文本）。</summary>
public sealed class EtaTextConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not long received || values[1] is not long total || total <= 0 || received >= total)
            return "--";
        if (values.Length > 2 && values[2] is long speed && speed > 0)
            return FormatUtils.TimeSpan((total - received) / speed);
        return "--";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>任务状态 → 是否可见（ConverterParameter 传状态名列表，逗号分隔）。</summary>
public sealed class StatusVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DownloadTaskStatus status || parameter is not string list)
            return Visibility.Collapsed;

        foreach (var name in list.Split(','))
        {
            if (Enum.TryParse<DownloadTaskStatus>(name.Trim(), true, out var s) && s == status)
                return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>进度/大小汇总文本："123 MB / 1.5 GB"。</summary>
public sealed class BytesProgressTextConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not long received)
            return "--";
        var total = values[1] as long? ?? 0;
        return total > 0 ? $"{FormatUtils.Bytes(received)} / {FormatUtils.Bytes(total)}" : FormatUtils.Bytes(received);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>字符串非空 → 可见。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>集合数量为 0 → 可见（空态提示）。</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>枚举值 == ConverterParameter 字符串 → bool（设置页主题三选一）。</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString()?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) == true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter != null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>字节/秒 ↔ MB/s（设置页限速输入）。</summary>
public sealed class BytesPerSecondToMbConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long b && b > 0 ? Math.Round(b / 1024.0 / 1024.0, 2) : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double mb && mb > 0 ? (long)(mb * 1024 * 1024) : 0L;
}
