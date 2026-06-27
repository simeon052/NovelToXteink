namespace NovelToEink.Core;

/// <summary>対応している小説サイト。</summary>
public enum NovelSite
{
    Syosetu,
    Kakuyomu,
}

/// <summary>小説のメタ情報（タイトル・作者・あらすじなど）。</summary>
public sealed class NovelMetadata
{
    public required NovelSite Site { get; init; }

    /// <summary>サイト内のID（なろうのNコード、カクヨムの作品ID）。</summary>
    public required string WorkId { get; init; }

    public required string Title { get; init; }

    public string Author { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>目次ページのURL。</summary>
    public required string Url { get; init; }

    /// <summary>サイトが公式に提供する表紙画像URL（カクヨムなど）。無ければ空。</summary>
    public string? OfficialCoverUrl { get; init; }

    /// <summary>完結済みか（なろう: end==0、カクヨム: serialStatus==COMPLETED）。</summary>
    public bool IsCompleted { get; init; }

    /// <summary>サイト上の最終更新日時（最終話の投稿/更新日）。</summary>
    public DateTimeOffset? SiteLastUpdated { get; init; }
}

/// <summary>目次上の1エピソードへの参照。</summary>
public sealed class EpisodeRef
{
    /// <summary>1始まりの通し番号。</summary>
    public required int Index { get; init; }

    public required string Title { get; init; }

    public required string Url { get; init; }

    /// <summary>章（グループ）見出し。無い場合はnull。</summary>
    public string? ChapterTitle { get; init; }
}

/// <summary>1エピソードの本文。各HTMLフラグメントはサニタイズ済みのXHTML断片。</summary>
public sealed class EpisodeContent
{
    public required EpisodeRef Ref { get; init; }

    /// <summary>前書き（無ければnull）。</summary>
    public string? ForewordHtml { get; init; }

    /// <summary>本文。</summary>
    public required string BodyHtml { get; init; }

    /// <summary>後書き（無ければnull）。</summary>
    public string? AfterwordHtml { get; init; }

    /// <summary>本文中で参照される画像URL（挿絵）。</summary>
    public IReadOnlyList<string> ImageUrls { get; init; } = [];
}

/// <summary>ダウンロードして取り込んだ画像1枚。</summary>
public sealed class ScrapedImage
{
    public required string SourceUrl { get; init; }
    public required byte[] Data { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>挿絵が登場するエピソード番号（公式表紙など本文外は0）。</summary>
    public int EpisodeIndex { get; init; }

    /// <summary>公式表紙画像か。</summary>
    public bool IsOfficialCover { get; init; }
}

/// <summary>1作品分のダウンロード結果。</summary>
public sealed class NovelDownload
{
    public required NovelMetadata Metadata { get; init; }
    public required IReadOnlyList<EpisodeContent> Episodes { get; init; }

    /// <summary>表紙候補として選べるスクレイピング済み画像。</summary>
    public required IReadOnlyList<ScrapedImage> Images { get; init; }
}
