using NovelToEink.Core;
using Xunit;

namespace NovelToEink.Tests;

/// <summary>キーワード検索のパーサと並べ替え（ネットワーク不要）。</summary>
public class SearchTests
{
    // --- なろう novel API（of=t-n-w-ga-e） ---

    [Fact]
    public void ParseSyosetuJson_SkipsHeaderAndReadsCountAndStatus()
    {
        // 実際の応答形状: 先頭は {"allcount":N} ヘッダ、以降は作品オブジェクト（値は文字列）
        var json = """
            [{"allcount":2,"with":2,"page":1,"rescount":2},
             {"ncode":"12345","title":"転生テスト","writer":"作者A","general_all_no":"42","end":"0"},
             {"ncode":"67890","title":"転生サンプル","writer":"作者B","general_all_no":"7","end":"1"}]
            """;

        var results = NovelSearchService.ParseSyosetuJson(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("https://ncode.syosetu.com/12345/", results[0].Url);
        Assert.Equal(42, results[0].EpisodeCount);
        Assert.True(results[0].IsCompleted);                      // end=0 → 完結
        Assert.Equal(7, results[1].EpisodeCount);
        Assert.False(results[1].IsCompleted);                     // end=1 → 連載中
    }

    [Fact]
    public void ParseSyosetuJson_IgnoresItemsWithoutNcode()
    {
        var json = """
            [{"allcount":0,"with":0,"page":1,"rescount":0},
             {"title":"ncodeなし","writer":"x","general_all_no":"1","end":"0"}]
            """;
        Assert.Empty(NovelSearchService.ParseSyosetuJson(json));
    }

    // --- カクヨム検索ページ（埋め込み JSON エンティティ） ---

    [Fact]
    public void ParseKakuyomuHtml_ReadsEntityStatusAndAuthor()
    {
        var html = """
            <a title="転生リスト" href="/works/111">リンク</a>
            {"entities":{"Work:111":{"title":"転生リスト","publicEpisodeCount":42,"serialStatus":"COMPLETED","author":{"__ref":"UserAccount:7"}},"UserAccount:7":{"activityName":"作者A"}}}
            """;

        var results = NovelSearchService.ParseKakuyomuHtml(html, 20);

        Assert.Single(results);
        Assert.Equal("転生リスト", results[0].Title);
        Assert.Equal("作者A", results[0].Author);
        Assert.Equal(42, results[0].EpisodeCount);
        Assert.True(results[0].IsCompleted);
        Assert.Equal(NovelSite.Kakuyomu, results[0].Site);
        Assert.Equal("https://kakuyomu.jp/works/111", results[0].Url);
    }

    [Fact]
    public void ParseKakuyomuHtml_RunningWorkAndBracesInStrings()
    {
        // 文字列内の { } は深さ計算で無視されること
        var html = """
            <a title="折れ括弧" href="/works/222">リンク</a>
            {"entities":{"Work:222":{"title":"折れ括弧{注意}","publicEpisodeCount":"3","serialStatus":"RUNNING","author":{"__ref":"UserAccount:8"}},"UserAccount:8":{"activityName":"作者B"}}}
            """;

        var results = NovelSearchService.ParseKakuyomuHtml(html, 20);

        Assert.Single(results);
        Assert.Equal(3, results[0].EpisodeCount);                 // 文字列 "3" も話数として読む
        Assert.False(results[0].IsCompleted);                     // RUNNING → 連載中
        Assert.Equal("作者B", results[0].Author);
    }

    [Fact]
    public void ParseKakuyomuHtml_DedupesAndRespectsMaxCount()
    {
        var html = """
            <a title="A" href="/works/1">x</a>
            <a title="B" href="/works/1">x</a>
            <a title="C" href="/works/2">x</a>
            <a title="D" href="/works/3">x</a>
            """;

        var results = NovelSearchService.ParseKakuyomuHtml(html, 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("A", results[0].Title);
        Assert.Equal("C", results[1].Title);
    }

    // --- 並べ替え（タイトル一致優先） ---

    [Fact]
    public void Rank_PrefersExactTitleMatch()
    {
        var results = new List<NovelSearchResult>
        {
            new(NovelSite.Syosetu, "雑談 転生 について", "x", "https://ncode.syosetu.com/1/"),
            new(NovelSite.Syosetu, "転生", "y", "https://ncode.syosetu.com/2/"),
            new(NovelSite.Syosetu, "俺の転生", "z", "https://ncode.syosetu.com/3/"),
        };

        var ranked = NovelSearchService.Rank(results, "転生", 10);

        Assert.Equal("転生", ranked[0].Title);          // 完全一致が先頭
        Assert.Equal("俺の転生", ranked[1].Title);      // 部分一致（短い順）
        Assert.Equal("雑談 転生 について", ranked[2].Title);
    }
}
