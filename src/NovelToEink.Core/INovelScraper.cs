namespace NovelToEink.Core;

/// <summary>小説サイトごとのスクレイパー。</summary>
public interface INovelScraper
{
    NovelSite Site { get; }

    /// <summary>このURLを処理できるか。</summary>
    bool CanHandle(string url);

    /// <summary>URLからサイトとワークIDを解析する（ネットワーク不要）。対応URLでなければ null。</summary>
    (NovelSite site, string workId)? TryParseWorkId(string url);

    /// <summary>目次URLからメタ情報を取得。</summary>
    Task<NovelMetadata> GetMetadataAsync(string url, CancellationToken ct = default);

    /// <summary>全エピソードの一覧（順序付き）を取得。</summary>
    Task<IReadOnlyList<EpisodeRef>> GetTableOfContentsAsync(string url, CancellationToken ct = default);

    /// <summary>1エピソードの本文を取得。</summary>
    Task<EpisodeContent> GetEpisodeAsync(EpisodeRef episode, EpubOptions options, CancellationToken ct = default);
}
