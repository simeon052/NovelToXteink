namespace NovelToEink.Core;

/// <summary>
/// Web 検索で拾った表紙候補をディスクに置いておく。
/// 追加時は暫定表紙で先へ進み、候補は裏で集めてキャッシュしておくことで、
/// あとから表紙を差し替えるときに待たされないようにする。
/// </summary>
public static class CoverCandidateCache
{
    /// <summary>この作品の候補を置くディレクトリ。</summary>
    public static string GetDirectory(string outputFolder, NovelSite site, string workId)
        => Path.Combine(DownloadCache.GetCacheRoot(outputFolder), "covers", $"{site}_{NameFormatter.Sanitize(workId)}");

    /// <summary>キャッシュされている候補の件数。画像は読み込まずファイル数だけ数える。</summary>
    public static int CountCandidates(string outputFolder, NovelSite site, string workId)
    {
        try
        {
            var dir = GetDirectory(outputFolder, site, workId);
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.img").Count() : 0;
        }
        catch { return 0; }
    }

    /// <summary>候補を保存する。既存のキャッシュは置き換える。</summary>
    public static void Save(string outputFolder, NovelSite site, string workId, IEnumerable<ScrapedImage> images)
    {
        var dir = GetDirectory(outputFolder, site, workId);
        Directory.CreateDirectory(dir);

        foreach (var stale in Directory.EnumerateFiles(dir, "*.img"))
            TryDelete(stale);

        var index = 0;
        foreach (var image in images)
        {
            if (image.Data.Length == 0) continue;
            try
            {
                File.WriteAllBytes(Path.Combine(dir, $"{index:D2}.img"), image.Data);
                File.WriteAllText(Path.Combine(dir, $"{index:D2}.url"), image.SourceUrl ?? "");
                index++;
            }
            catch { /* キャッシュなので失敗しても致命的ではない */ }
        }
    }

    /// <summary>キャッシュ済みの候補を読み出す。無ければ空。</summary>
    public static List<ScrapedImage> Load(string outputFolder, NovelSite site, string workId)
    {
        var results = new List<ScrapedImage>();
        var dir = GetDirectory(outputFolder, site, workId);
        if (!Directory.Exists(dir)) return results;

        foreach (var path in Directory.EnumerateFiles(dir, "*.img").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                var data = File.ReadAllBytes(path);
                var urlPath = Path.ChangeExtension(path, ".url");
                var sourceUrl = File.Exists(urlPath) ? File.ReadAllText(urlPath) : path;
                var image = ImageProcessor.TryLoad(data, string.IsNullOrWhiteSpace(sourceUrl) ? path : sourceUrl);
                if (image != null) results.Add(image);
            }
            catch { /* 壊れたキャッシュは飛ばす */ }
        }

        return results;
    }

    /// <summary>この作品の候補キャッシュを消す。</summary>
    public static void Clear(string outputFolder, NovelSite site, string workId)
    {
        var dir = GetDirectory(outputFolder, site, workId);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
