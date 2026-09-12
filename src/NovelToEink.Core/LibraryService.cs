using System.IO;
using System.Text.Json;

namespace NovelToEink.Core;

/// <summary>
/// ライブラリ（指定フォルダ）に対するダウンロード・EPUB生成・更新チェック・更新を統括する。
/// 表紙の対話的選択はUI側で行い、結果(ScrapedImage?)をここへ渡す。UI非依存。
/// </summary>
public sealed class LibraryService
{
    public AppSettings Settings { get; }
    public LibraryStore Store { get; private set; }
    public List<LibraryEntry> Entries { get; private set; }

    public LibraryService(AppSettings settings)
    {
        Settings = settings;
        Store = new LibraryStore(settings.OutputFolder);
        Entries = Store.Load();
    }

    /// <summary>保存先フォルダを切り替え、その配下のライブラリを読み込む。</summary>
    public void ChangeFolder(string folder)
    {
        Settings.OutputFolder = folder;
        Settings.Save();
        Store = new LibraryStore(folder);
        Entries = Store.Load();
    }

    public void Persist() => Store.Save(Entries);

    public bool IsSupported(string url) => new[] { "ncode.syosetu.com", "novel18.syosetu.com", "kakuyomu.jp" }
        .Any(h => url.Contains(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>作品を丸ごとダウンロードする（表紙選択のため呼び出し側に返す）。キャッシュを使用。</summary>
    public async Task<NovelDownload> DownloadAsync(
        string url, EpubOptions epubOptions, IProgress<DownloadProgress>? progress, CancellationToken ct,
        int maxEpisodes = 0, bool forceRefreshMeta = false)
    {
        var dlOpt = new DownloadOptions { RequestDelayMs = Settings.RequestDelayMs, DownloadImages = true, MaxEpisodes = maxEpisodes };
        using var service = new NovelDownloadService(dlOpt);
        var cacheRoot = DownloadCache.GetCacheRoot(Settings.OutputFolder);
        return await service.DownloadAsync(url, epubOptions, dlOpt, progress, ct, cacheRoot, forceRefreshMeta);
    }

    /// <summary>ダウンロード済みデータと選択表紙からEPUB（必要なら分割）を生成し、ライブラリへ登録/更新する。</summary>
    public LibraryEntry BuildAndRegister(
        NovelDownload novel, ScrapedImage? cover, EpubOptions epubOptions, IProgress<string>? progress)
    {
        var meta = novel.Metadata;
        var existing = Entries.FirstOrDefault(e => e.Url == meta.Url || SameWork(e, meta));
        var nameTemplate = existing?.NameTemplate ?? NameFormatter.DefaultTemplate;

        // 表紙サイドカー（更新時に再利用）。名前はテンプレートに依存しない安定名にする。
        var coverBase = NameFormatter.Sanitize($"{meta.Title}_{meta.WorkId}");
        string? coverPath = existing?.CoverImagePath;
        if (cover != null)
        {
            coverPath = Path.Combine(Settings.OutputFolder, coverBase + ".cover.jpg");
            File.WriteAllBytes(coverPath, cover.Data);
        }
        else
        {
            if (existing?.CoverImagePath is { } oc && File.Exists(oc)) File.Delete(oc);
            coverPath = null;
        }

        // 旧パートを掃除してから生成（話数変化で分割数が変わるため）
        if (existing != null)
            foreach (var old in existing.EpubParts)
                TryDelete(old);

        var proofreading = epubOptions.EnableProofreading ? ProofreadingService.Load() : null;
        var parts = EpubBuilder.BuildSplit(novel, cover, epubOptions, Settings.OutputFolder, nameTemplate, progress, proofreading);

        var entry = existing ?? new LibraryEntry { NameTemplate = nameTemplate };
        entry.Url = meta.Url;
        entry.Site = meta.Site;
        entry.WorkId = meta.WorkId;
        entry.Title = meta.Title;
        entry.Author = meta.Author;
        entry.Description = meta.Description;
        entry.IsCompleted = meta.IsCompleted;
        entry.SiteLastUpdated = meta.SiteLastUpdated;
        entry.EpisodeCount = novel.Episodes.Count;
        entry.LastEpisodeTitle = novel.Episodes.Count > 0 ? novel.Episodes[^1].Ref.Title : "";
        entry.EpubParts = parts.Select(p => p.OutputPath).ToList();
        entry.EpubPath = entry.EpubParts.FirstOrDefault() ?? "";
        entry.CoverImagePath = coverPath;
        entry.Options = epubOptions;
        entry.LastUpdatedAt = DateTimeOffset.Now;
        entry.LastCheckedAt = DateTimeOffset.Now;
        entry.Status = UpdateStatus.UpToDate;

        if (existing == null) Entries.Add(entry);
        Persist();
        return entry;
    }

    /// <summary>出力ファイル名テンプレートを変更し、既存の分割ファイルをリネームする。</summary>
    public void RenameFiles(LibraryEntry entry, string newTemplate)
    {
        if (string.IsNullOrWhiteSpace(newTemplate)) newTemplate = NameFormatter.DefaultTemplate;
        var count = entry.EpubParts.Count;
        var newPaths = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var old = entry.EpubParts[i];
            var newName = NameFormatter.Format(newTemplate, entry.Title, entry.Author, i + 1, count) + ".epub";
            var newPath = Path.Combine(Settings.OutputFolder, newName);
            if (!string.Equals(old, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(old))
            {
                try
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(old, newPath);
                }
                catch { newPath = old; }
            }
            newPaths.Add(File.Exists(newPath) ? newPath : old);
        }
        entry.NameTemplate = newTemplate;
        entry.EpubParts = newPaths;
        entry.EpubPath = newPaths.FirstOrDefault() ?? entry.EpubPath;
        Persist();
    }

    /// <summary>更新チェック：メタ情報と目次を取得して、完結状態・最終更新日・話数差分を反映する。</summary>
    public async Task<bool> CheckAsync(LibraryEntry entry, CancellationToken ct)
    {
        var dlOpt = new DownloadOptions { RequestDelayMs = Settings.RequestDelayMs };
        using var service = new NovelDownloadService(dlOpt);
        var meta = await service.GetMetadataAsync(entry.Url, ct);
        var toc = await service.GetTableOfContentsAsync(entry.Url, ct);

        entry.Title = meta.Title;
        entry.Author = meta.Author;
        entry.IsCompleted = meta.IsCompleted;
        entry.SiteLastUpdated = meta.SiteLastUpdated;
        var hasUpdate = entry.HasUpdate(toc);
        entry.LastCheckedAt = DateTimeOffset.Now;
        entry.Status = hasUpdate ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
        Persist();
        return hasUpdate;
    }

    /// <summary>既存エントリを再ダウンロードしてEPUBを作り直す（表紙はサイドカーを再利用）。</summary>
    public async Task UpdateAsync(LibraryEntry entry, IProgress<DownloadProgress>? dlProgress,
        IProgress<string>? buildProgress, CancellationToken ct)
    {
        // 更新時は常に最新のメタデータを取得する（完結・タイトル変更を逃さないため）
        var novel = await DownloadAsync(entry.Url, entry.Options, dlProgress, ct, forceRefreshMeta: true);

        ScrapedImage? cover = null;
        if (entry.CoverImagePath is { } cp && File.Exists(cp))
            cover = ImageProcessor.TryLoad(await File.ReadAllBytesAsync(cp, ct), "file:cover");

        // 旧パートを掃除してから再生成
        foreach (var old in entry.EpubParts) TryDelete(old);
        var proofreading2 = entry.Options.EnableProofreading ? ProofreadingService.Load() : null;
        var parts = EpubBuilder.BuildSplit(novel, cover, entry.Options, Settings.OutputFolder, entry.NameTemplate, buildProgress, proofreading2);

        entry.Title = novel.Metadata.Title;
        entry.Author = novel.Metadata.Author;
        entry.Description = novel.Metadata.Description;
        entry.IsCompleted = novel.Metadata.IsCompleted;
        entry.SiteLastUpdated = novel.Metadata.SiteLastUpdated;
        entry.EpisodeCount = novel.Episodes.Count;
        entry.LastEpisodeTitle = novel.Episodes.Count > 0 ? novel.Episodes[^1].Ref.Title : "";
        entry.EpubParts = parts.Select(p => p.OutputPath).ToList();
        entry.EpubPath = entry.EpubParts.FirstOrDefault() ?? entry.EpubPath;
        entry.LastUpdatedAt = DateTimeOffset.Now;
        entry.LastCheckedAt = DateTimeOffset.Now;
        entry.Status = UpdateStatus.UpToDate;
        Persist();
    }

    public void Remove(LibraryEntry entry, bool deleteFiles)
    {
        Entries.Remove(entry);
        if (deleteFiles)
        {
            foreach (var p in entry.EpubParts) TryDelete(p);
            TryDelete(entry.EpubPath);
            if (entry.CoverImagePath != null) TryDelete(entry.CoverImagePath);
            // キャッシュディレクトリも削除
            var cacheDir = DownloadCache.GetWorkCacheDir(
                DownloadCache.GetCacheRoot(Settings.OutputFolder),
                entry.Site.ToString(), entry.WorkId);
            TryDeleteDirectory(cacheDir);
        }
        Persist();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static bool SameWork(LibraryEntry e, NovelMetadata m) => e.Site == m.Site && e.WorkId == m.WorkId;

    private string ResolveBaseName(NovelMetadata meta, LibraryEntry? existing)
    {
        if (existing != null && !string.IsNullOrEmpty(existing.EpubPath))
            return Path.GetFileNameWithoutExtension(existing.EpubPath);

        var baseName = SanitizeFileName(meta.Title);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = meta.WorkId;
        if (Entries.Any(e => string.Equals(Path.GetFileNameWithoutExtension(e.EpubPath), baseName,
                StringComparison.OrdinalIgnoreCase) && !SameWork(e, meta)))
            baseName += "_" + meta.WorkId;
        return baseName;
    }

    public static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    /// <summary>
    /// ライブラリ内の全EPUBを縦書き・目次全角スペース修正・テキスト置換でパッチする（再ダウンロードなし）。
    /// proofreading.json の有効な text_replace ルールを本文 XHTML にも適用する。
    /// </summary>
    public void PatchAllVertical(IProgress<string>? progress = null)
    {
        var textReplacements = ProofreadingService.Load().GetEnabledTextReplacements();
        foreach (var entry in Entries)
        {
            foreach (var path in entry.EpubParts.Where(File.Exists))
            {
                progress?.Report($"パッチ中: {Path.GetFileName(path)}");
                try
                {
                    entry.Options.WritingMode = WritingMode.Vertical;
                    EpubBuilder.PatchVertical(path, entry.Options, textReplacements);
                }
                catch (Exception ex)
                {
                    progress?.Report($"スキップ: {Path.GetFileName(path)} ({ex.Message})");
                }
            }
        }
        Persist();
    }

    /// <summary>ライブラリと設定をJSONファイルへエクスポートする。</summary>
    public void ExportLibrary(string filePath)
    {
        var exportData = new
        {
            ExportedAt = DateTimeOffset.Now,
            AppVersion = "1.0",
            Library = Entries,
            Settings = new
            {
                Settings.Vertical,
                Settings.GrayscaleImages,
                Settings.IncludeInlineImages,
                Settings.KeepRuby,
                Settings.EnableProofreading,
                Settings.EpisodesPerFile,
                Settings.RequestDelayMs,
                Settings.IsDarkMode,
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(exportData, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>エクスポートしたJSONからライブラリと設定をインポートする。</summary>
    public (List<LibraryEntry> Library, Dictionary<string, object> Settings) ImportLibrary(string filePath)
    {
        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var libraryJson = root.GetProperty("Library").GetRawText();
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var library = JsonSerializer.Deserialize<List<LibraryEntry>>(libraryJson, options) ?? [];

        var settingsObj = root.GetProperty("Settings");
        var settings = new Dictionary<string, object>();
        foreach (var prop in settingsObj.EnumerateObject())
        {
            settings[prop.Name] = prop.Value.GetRawText();
        }

        return (library, settings);
    }

    /// <summary>エクスポートしたJSONの設定をアプリ設定に反映させる。</summary>
    public static void ApplyImportedSettings(AppSettings settings, Dictionary<string, object> importedSettings)
    {
        if (importedSettings.TryGetValue("Vertical", out var v) && bool.TryParse(v.ToString(), out var vertical))
            settings.Vertical = vertical;
        if (importedSettings.TryGetValue("GrayscaleImages", out var g) && bool.TryParse(g.ToString(), out var grayscale))
            settings.GrayscaleImages = grayscale;
        if (importedSettings.TryGetValue("IncludeInlineImages", out var i) && bool.TryParse(i.ToString(), out var inline))
            settings.IncludeInlineImages = inline;
        if (importedSettings.TryGetValue("KeepRuby", out var r) && bool.TryParse(r.ToString(), out var ruby))
            settings.KeepRuby = ruby;
        if (importedSettings.TryGetValue("EnableProofreading", out var p) && bool.TryParse(p.ToString(), out var proof))
            settings.EnableProofreading = proof;
        if (importedSettings.TryGetValue("EpisodesPerFile", out var e) && int.TryParse(e.ToString(), out var episodes))
            settings.EpisodesPerFile = episodes;
        if (importedSettings.TryGetValue("RequestDelayMs", out var d) && int.TryParse(d.ToString(), out var delay))
            settings.RequestDelayMs = delay;
        if (importedSettings.TryGetValue("IsDarkMode", out var dark) && bool.TryParse(dark.ToString(), out var darkMode))
            settings.IsDarkMode = darkMode;
    }
}
