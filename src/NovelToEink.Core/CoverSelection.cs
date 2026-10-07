namespace NovelToEink.Core;

/// <summary>表紙の自動選択。</summary>
public static class CoverSelection
{
    /// <summary>
    /// ユーザーに尋ねずに使う暫定表紙を選ぶ。
    /// 公式表紙があればそれ、無ければタイトルから生成した文字表紙。どちらも作れなければ null。
    /// </summary>
    public static ScrapedImage? PickProvisional(NovelDownload novel, EpubOptions options)
    {
        var official = novel.Images.FirstOrDefault(i => i.IsOfficialCover);
        if (official != null) return official;

        try { return ImageProcessor.GenerateTextCover(novel.Metadata.Title, novel.Metadata.Author, options); }
        catch (Exception ex) { CoreLog.Warn("文字表紙を作れないので簡易表紙にする（フォント不備など）", ex); }

        try { return ImageProcessor.GenerateMinimalCover(); }
        catch (Exception ex) { CoreLog.Error("簡易表紙も作れない", ex); return null; }
    }
}
