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
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim();
        return string.IsNullOrEmpty(s) ? "novel" : s;
    }
}
