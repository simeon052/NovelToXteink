using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelToEink.Core;

/// <summary>
/// エピソード本文と画像をローカルにキャッシュし、再取得を差分のみにする。
/// <br/>キャッシュは {OutputFolder}/.cache/{site}_{workId}/ 配下に置く。
/// </summary>
public sealed class DownloadCache
{
    private static readonly TimeSpan MetaTtl = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dir;

    public DownloadCache(string cacheRoot, string site, string workId)
    {
        _dir = GetWorkCacheDir(cacheRoot, site, workId);
        Directory.CreateDirectory(_dir);
    }

    /// <summary>キャッシュルートは OutputFolder 直下の .cache/。</summary>
    public static string GetCacheRoot(string outputFolder)
        => Path.Combine(outputFolder, ".cache");

    /// <summary>特定作品のキャッシュフォルダパス。</summary>
    public static string GetWorkCacheDir(string cacheRoot, string site, string workId)
        => Path.Combine(cacheRoot, $"{Sanitize(site)}_{Sanitize(workId)}");

    // ---- メタデータ ----

    /// <summary>
    /// キャッシュからメタデータを読む。TTL（1時間）超過または存在しない場合は null。
    /// </summary>
    public NovelMetadata? TryGetMetadata()
    {
        var path = MetaPath();
        if (!File.Exists(path)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<MetaCacheEntry>(File.ReadAllText(path), JsonOpts);
            if (dto is null) return null;
            if (DateTimeOffset.Now - dto.CachedAt > MetaTtl) return null;
            return dto.ToMetadata();
        }
        catch { return null; }
    }

    /// <summary>メタデータをキャッシュに保存する。失敗は無視。</summary>
    public void SetMetadata(NovelMetadata meta)
    {
        try
        {
            File.WriteAllText(MetaPath(), JsonSerializer.Serialize(MetaCacheEntry.From(meta), JsonOpts));
        }
        catch { }
    }

    // ---- エピソード ----

    /// <summary>キャッシュからエピソードを読む。キャッシュミスや URL 不一致（削除/差し替え）は null。</summary>
    public EpisodeContent? TryGetEpisode(EpisodeRef ep)
    {
        var path = EpisodePath(ep.Index);
        if (!File.Exists(path)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<EpisodeCacheEntry>(File.ReadAllText(path), JsonOpts);
            if (dto is null) return null;
            // URL が変わっていたらキャッシュミスとして扱う（エピソード差し替え対応）
            if (dto.EpisodeUrl != null && dto.EpisodeUrl != ep.Url) return null;
            return dto.ToContent(ep);
        }
        catch { return null; }
    }

    /// <summary>エピソードをキャッシュに保存する。失敗は無視。</summary>
    public void SetEpisode(EpisodeContent content)
    {
        try
        {
            var dto = EpisodeCacheEntry.From(content);
            File.WriteAllText(EpisodePath(content.Ref.Index), JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch { }
    }

    // ---- 画像 ----

    /// <summary>URL を鍵として画像バイト列を読む。キャッシュミスは null。</summary>
    public byte[]? TryGetImage(string url)
    {
        var path = ImagePath(url);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    /// <summary>画像をキャッシュに保存する。失敗は無視。</summary>
    public void SetImage(string url, byte[] data)
    {
        try { File.WriteAllBytes(ImagePath(url), data); }
        catch { }
    }

    // ---- パス計算 ----

    private string MetaPath() => Path.Combine(_dir, "meta.json");
    private string EpisodePath(int index) => Path.Combine(_dir, $"ep_{index:0000}.json");

    private string ImagePath(string url)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_dir, $"img_{hash}.bin");
    }

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    // ---- DTO ----

    private sealed record MetaCacheEntry(
        DateTimeOffset CachedAt,
        NovelSite Site,
        string WorkId,
        string Title,
        string Author,
        string Description,
        string Url,
        bool IsCompleted,
        DateTimeOffset? SiteLastUpdated,
        string? OfficialCoverUrl)
    {
        public static MetaCacheEntry From(NovelMetadata m) => new(
            DateTimeOffset.Now, m.Site, m.WorkId, m.Title, m.Author, m.Description,
            m.Url, m.IsCompleted, m.SiteLastUpdated, m.OfficialCoverUrl);

        public NovelMetadata ToMetadata() => new()
        {
            Site = Site,
            WorkId = WorkId,
            Title = Title,
            Author = Author,
            Description = Description,
            Url = Url,
            IsCompleted = IsCompleted,
            SiteLastUpdated = SiteLastUpdated,
            OfficialCoverUrl = OfficialCoverUrl,
        };
    }

    private sealed record EpisodeCacheEntry(
        string? EpisodeUrl,
        string? ForewordHtml,
        string BodyHtml,
        string? AfterwordHtml,
        string[]? ImageUrls)
    {
        public static EpisodeCacheEntry From(EpisodeContent c) => new(
            c.Ref.Url, c.ForewordHtml, c.BodyHtml, c.AfterwordHtml, [.. c.ImageUrls]);

        public EpisodeContent ToContent(EpisodeRef ep) => new()
        {
            Ref = ep,
            ForewordHtml = ForewordHtml,
            BodyHtml = BodyHtml,
            AfterwordHtml = AfterwordHtml,
            ImageUrls = ImageUrls ?? [],
        };
    }
}
