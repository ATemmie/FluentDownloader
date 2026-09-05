using System.Text;

namespace FluentDownloader.Core;

/// <summary>Content-Disposition / URL 文件名解析 + Windows 文件名清洗。</summary>
public static class FileNameParser
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
    };

    /// <summary>解析 Content-Disposition 头，优先 filename*（RFC 5987），退回 filename。</summary>
    public static string? FromContentDisposition(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        // filename*=UTF-8''%E5%90%8D.ext
        var star = header.IndexOf("filename*=", StringComparison.OrdinalIgnoreCase);
        if (star >= 0)
        {
            var v = header[(star + "filename*=".Length)..].Trim().Trim('"', ';', ' ');
            var quote = v.IndexOf('\'');
            if (quote >= 0)
            {
                var second = v.IndexOf('\'', quote + 1);
                if (second > quote)
                {
                    var encoded = v[(second + 1)..];
                    try { return Uri.UnescapeDataString(encoded); } catch { /* fallthrough */ }
                }
            }
        }

        var plain = header.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
        if (plain >= 0)
        {
            var v = header[(plain + "filename=".Length)..].Split(';')[0].Trim().Trim('"');
            if (v.Length > 0) return v;
        }

        return null;
    }

    /// <summary>从 URL 末段提取文件名（剥 query、URL 解码）。</summary>
    public static string FromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var last = Uri.UnescapeDataString(uri.Segments[^1]).TrimEnd('/');
            return last.Length > 0 ? last : "download.bin";
        }
        catch
        {
            return "download.bin";
        }
    }

    /// <summary>移除 Windows 非法字符、保留名、结尾点/空格，并限制长度。</summary>
    public static string Sanitize(string name, int maxLength = 160)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

        var result = sb.ToString().Trim();
        foreach (var reserved in ReservedNames)
        {
            if (string.Equals(result, reserved, StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith(reserved + ".", StringComparison.OrdinalIgnoreCase))
            {
                result = "_" + result;
                break;
            }
        }

        if (result.Length == 0) result = "download.bin";
        if (result.Length > maxLength)
        {
            var ext = Path.GetExtension(result);
            result = result[..Math.Max(1, maxLength - ext.Length)] + ext;
        }
        return result.TrimEnd(' ', '.');
    }

    /// <summary>目录内重名时追加 " (n)"。</summary>
    public static string EnsureUnique(string directory, string fileName)
    {
        try
        {
            if (!File.Exists(Path.Combine(directory, fileName))) return fileName;

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var i = 1; i < 1000; i++)
            {
                var candidate = $"{stem} ({i}){ext}";
                if (!File.Exists(Path.Combine(directory, candidate))) return candidate;
            }
        }
        catch
        {
            // 目录不可访问等场景直接返回原名，让下载过程自己报错
        }
        return fileName;
    }
}
