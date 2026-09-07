using SkiaSharp;

namespace NovelToEink.Xtc;

/// <summary>選択可能な描画フォント 1 件。</summary>
/// <param name="DisplayName">UI に出す名前。</param>
/// <param name="FilePath">フォントファイルの絶対パス。</param>
/// <param name="Source">取得元（同梱 / システム）。</param>
public sealed record XtcFont(string DisplayName, string FilePath, string Source)
{
    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}

/// <summary>
/// XTC 描画に使えるフォントの探索。
/// アプリ同梱の Font フォルダを優先し、次に Windows の日本語フォントを列挙する。
/// </summary>
public static class FontFinder
{
    // 先頭が自動選択の既定になる。1bit へ 2 値化する都合上、線が太く均一な書体ほど有利で、
    // 明朝の細い横画は小さめの文字サイズだと 1px になって飛びやすい。
    // 24px で同じ本文を描いた黒画素率（高いほど線が太い）:
    //   BIZ UDゴシック 3.58% / MS ゴシック 3.56% / 游明朝 Demibold 3.04% / MS 明朝 2.20%
    private static readonly (string File, string Name)[] WindowsJapaneseFonts =
    [
        ("BIZ-UDGothicR.ttc", "BIZ UDゴシック"),
        ("BIZ-UDMinchoM.ttc", "BIZ UD明朝 Medium"),
        ("YuGothM.ttc", "游ゴシック Medium"),
        ("meiryo.ttc", "メイリオ"),
        ("yumindb.ttf", "游明朝 Demibold"),
        ("HGRME.TTC", "HG明朝E"),
        ("msgothic.ttc", "MS ゴシック"),
        ("msmincho.ttc", "MS 明朝"),
        ("yumin.ttf", "游明朝"),
        ("yuminl.ttf", "游明朝 Light"),
        ("YuGothR.ttc", "游ゴシック"),
    ];

    private static readonly string[] FontExtensions = [".ttf", ".ttc", ".otf", ".otc"];

    /// <summary>
    /// 利用できるフォントを列挙する。同梱フォント → システムフォントの順。
    /// </summary>
    /// <param name="bundledFontDirectory">同梱フォントのディレクトリ。null なら実行ファイル横の Font フォルダを見る。</param>
    public static IReadOnlyList<XtcFont> Enumerate(string? bundledFontDirectory = null)
    {
        var results = new List<XtcFont>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in EnumerateBundledDirectories(bundledFontDirectory))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir)
                         .Where(f => FontExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(file))
                    results.Add(new XtcFont(Path.GetFileNameWithoutExtension(file), file, "同梱"));
            }
        }

        var systemDir = GetSystemFontDirectory();
        if (systemDir is not null)
        {
            foreach (var (file, name) in WindowsJapaneseFonts)
            {
                var path = Path.Combine(systemDir, file);
                if (File.Exists(path) && seen.Add(path))
                    results.Add(new XtcFont(name, path, "システム"));
            }
        }

        return results;
    }

    /// <summary>既定で使うフォントを 1 件返す。見つからなければ null。</summary>
    public static XtcFont? GetDefault(string? bundledFontDirectory = null)
        => Enumerate(bundledFontDirectory).FirstOrDefault();

    /// <summary>
    /// 設定されたフォントファイルを読み込む。パスが空・不正なら既定フォントに、
    /// それも無ければ日本語を描けるシステムフォントにフォールバックする。
    /// </summary>
    public static SKTypeface Load(string? fontFile, string? bundledFontDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(fontFile) && File.Exists(fontFile))
        {
            var typeface = SKTypeface.FromFile(fontFile);
            if (typeface is not null) return typeface;
        }

        var fallback = GetDefault(bundledFontDirectory);
        if (fallback is not null)
        {
            var typeface = SKTypeface.FromFile(fallback.FilePath);
            if (typeface is not null) return typeface;
        }

        // 最後の手段：日本語の一文字を描けるフォントを OS に問い合わせる。
        return SKFontManager.Default.MatchCharacter('あ') ?? SKTypeface.Default;
    }

    private static IEnumerable<string> EnumerateBundledDirectories(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            yield return explicitDirectory;

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Font");

        // リポジトリ直下の python/Font（開発時のフォント置き場）も拾う。
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "python", "Font");
            if (Directory.Exists(candidate))
            {
                yield return candidate;
                yield break;
            }
        }
    }

    private static string? GetSystemFontDirectory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        return Directory.Exists(dir) ? dir : null;
    }
}
