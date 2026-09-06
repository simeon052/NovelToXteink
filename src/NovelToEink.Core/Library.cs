using System.Text.Json;
using NovelToEink.Xtc;
using System.Text.Json.Serialization;

namespace NovelToEink.Core;

/// <summary>ライブラリ上の作品の更新状態。</summary>
public enum UpdateStatus
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    Error,
}

/// <summary>ライブラリに登録された1作品（JSON永続化される）。</summary>
public sealed class LibraryEntry
{
    public string Url { get; set; } = "";
    public NovelSite Site { get; set; }
    public string WorkId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>取得済み話数。</summary>
    public int EpisodeCount { get; set; }

    /// <summary>更新検知用：最終話のタイトル。</summary>
    public string LastEpisodeTitle { get; set; } = "";

    /// <summary>出力したEPUB（分割時は先頭ファイル）の絶対パス。</summary>
    public string EpubPath { get; set; } = "";

    /// <summary>分割した各EPUBの絶対パス（分割しない場合は1件）。</summary>
    public List<string> EpubParts { get; set; } = [];

    /// <summary>出力ファイル名テンプレート（{title}/{author}/{part}）。</summary>
    public string NameTemplate { get; set; } = NameFormatter.DefaultTemplate;

    /// <summary>完結済みか。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>サイト上の最終更新日時。</summary>
    public DateTimeOffset? SiteLastUpdated { get; set; }

    /// <summary>表紙サイドカー画像の絶対パス（表紙なしなら null）。</summary>
    public string? CoverImagePath { get; set; }

    /// <summary>このエントリの生成に使ったEPUBオプション（更新時に再利用）。</summary>
    public EpubOptions Options { get; set; } = new();

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }

    /// <summary>直近の更新チェック結果（永続化はするが起動時は参考値）。</summary>
    public UpdateStatus Status { get; set; } = UpdateStatus.Unknown;

    /// <summary>目次から更新の有無を判定する。</summary>
    public bool HasUpdate(IReadOnlyList<EpisodeRef> toc)
    {
        if (toc.Count == 0) return false;
        if (toc.Count != EpisodeCount) return true;
        return toc[^1].Title != LastEpisodeTitle;
    }
}

/// <summary>指定フォルダ配下の library.json を読み書きする。</summary>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Folder { get; }
    public string FilePath => Path.Combine(Folder, "library.json");

    public LibraryStore(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
    }

    public List<LibraryEntry> Load()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<LibraryEntry>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<LibraryEntry> entries)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(entries.ToList(), JsonOpts);
        File.WriteAllText(FilePath, json);
    }
}

/// <summary>アプリ設定（%AppData%\NovelToEink\settings.json）。</summary>
public sealed class AppSettings
{
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NovelToEink");

    public int RequestDelayMs { get; set; } = 1500;

    // 新規追加時の既定オプション
    public bool Vertical { get; set; } = true;
    public bool GrayscaleImages { get; set; } = true;
    public bool IncludeInlineImages { get; set; } = true;
    public bool KeepRuby { get; set; } = true;
    public bool EnableProofreading { get; set; } = true;
    public int EpisodesPerFile { get; set; } = 200;
    public bool IsDarkMode { get; set; } = false;

    // XTC 出力
    /// <summary>EPUB 生成後に XTC も作るか。</summary>
    public bool GenerateXtc { get; set; } = false;

    /// <summary>XTC の出力対象端末（X3: 528x792 / X4 Pro: 480x800）。</summary>
    public XteinkDevice XtcDevice { get; set; } = XteinkDevice.X4Pro;

    /// <summary>XTC 描画に使うフォントファイル。空なら自動選択。</summary>
    public string XtcFontFile { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NovelToEink", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts) ?? new AppSettings();
        }
        catch { /* ignore */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* ignore */ }
    }

    public EpubOptions ToEpubOptions() => new()
    {
        WritingMode = Vertical ? Core.WritingMode.Vertical : Core.WritingMode.Horizontal,
        GrayscaleImages = GrayscaleImages,
        IncludeInlineImages = IncludeInlineImages,
        KeepRuby = KeepRuby,
        EpisodesPerFile = EpisodesPerFile,
        EnableProofreading = EnableProofreading,
        GenerateXtc = GenerateXtc,
        XtcDevice = XtcDevice,
        XtcFontFile = XtcFontFile,
    };
}
