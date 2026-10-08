using System.IO;

namespace NovelToEink.Core;

/// <summary>出力ファイル名テンプレートの整形。プレースホルダ {title} {author} {part} を展開する。</summary>
public static class NameFormatter
{
    public const string DefaultTemplate = "{title}-{part}-{author}";

    /// <summary>テンプレートからファイル名（拡張子なし・安全な文字）を作る。</summary>
    public static string Format(string template, string title, string author, int part, int partCount)
    {
        if (string.IsNullOrWhiteSpace(template)) template = DefaultTemplate;
        var width = Math.Max(2, partCount.ToString().Length);
        var name = template
            .Replace("{title}", title)
            .Replace("{author}", author)
            .Replace("{part}", part.ToString().PadLeft(width, '0'));
        return Sanitize(name);
    }

    public static string Sanitize(string s)
    {
        s = s.Replace('\u3000', ' ');   // 全角スペース → 半角スペース
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim();

        // 末尾のドット・空白を削る（Windows は末尾ドットを許さない）
        while (s.Length > 0 && (s[^1] == '.' || char.IsWhiteSpace(s[^1])))
            s = s[..^1];
        s = s.TrimEnd();

        // Windows の予約名 → 先頭に _ を付ける
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        if (reserved.Contains(s))
            s = "_" + s;

        return string.IsNullOrEmpty(s) ? "novel" : s;
    }
}
