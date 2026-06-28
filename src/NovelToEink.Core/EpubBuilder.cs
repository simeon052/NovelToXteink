using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace NovelToEink.Core;

/// <summary>
/// Xteink X3 のような非力な E-Ink 端末向けに「軽い」EPUB3 を生成する。
/// ・スタイルシートは最小限（巨大CSSのRAM展開で落ちる端末対策）
/// ・1エピソード=1XHTML（1ファイルあたりのDOMを小さく保つ）
/// ・画像はJPEG（WebP不可の端末向け）
/// </summary>
public static partial class EpubBuilder
{
    [GeneratedRegex(@"<img\b[^>]*\bsrc=""https?:[^""]*""[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LeftoverImgRegex();

    public sealed class BuildResult
    {
        public required string OutputPath { get; init; }
        public required int EpisodeCount { get; init; }
        public required int ImageCount { get; init; }
        public required long SizeBytes { get; init; }
    }

    /// <summary>1作品を（必要なら複数ファイルに分割して）生成する。</summary>
    /// <param name="outputDir">出力フォルダ。</param>
    /// <param name="nameTemplate">ファイル名テンプレート（{title}/{author}/{part}）。</param>
    /// <param name="proofreading">テキスト校正サービス（null なら校正しない）。</param>
    public static List<BuildResult> BuildSplit(
        NovelDownload novel,
        ScrapedImage? cover,
        EpubOptions options,
        string outputDir,
        string nameTemplate,
        IProgress<string>? progress = null,
        ProofreadingService? proofreading = null)
    {
        var size = options.EpisodesPerFile > 0 ? options.EpisodesPerFile : int.MaxValue;
        var all = novel.Episodes;
        var partCount = Math.Max(1, (int)Math.Ceiling(all.Count / (double)size));
        var meta = novel.Metadata;
        var results = new List<BuildResult>(partCount);

        for (var part = 1; part <= partCount; part++)
        {
            var subset = all.Skip((part - 1) * size).Take(size).ToList();
            var displayTitle = partCount > 1 ? $"{meta.Title}（{part}/{partCount}）" : meta.Title;
            var fileName = NameFormatter.Format(nameTemplate, meta.Title, meta.Author, part, partCount) + ".epub";
            var outPath = Path.Combine(outputDir, fileName);
            progress?.Report($"分割 {part}/{partCount} を生成中…");
            results.Add(BuildOne(meta, subset, novel.Images, cover, options, outPath, displayTitle, progress, part, partCount, proofreading));
        }
        return results;
    }

    /// <param name="cover">表紙画像。null なら表紙ページ無し。</param>
    /// <param name="proofreading">テキスト校正サービス（null なら校正しない）。</param>
    public static BuildResult Build(
        NovelDownload novel,
        ScrapedImage? cover,
        EpubOptions options,
        string outputPath,
        IProgress<string>? progress = null,
        ProofreadingService? proofreading = null)
        => BuildOne(novel.Metadata, novel.Episodes, novel.Images, cover, options, outputPath, novel.Metadata.Title, progress, 0, 0, proofreading);

    private static BuildResult BuildOne(
        NovelMetadata meta,
        IReadOnlyList<EpisodeContent> episodes,
        IReadOnlyList<ScrapedImage> allImages,
        ScrapedImage? cover,
        EpubOptions options,
        string outputPath,
        string displayTitle,
        IProgress<string>? progress,
        int part = 0,
        int partCount = 0,
        ProofreadingService? proofreading = null)
    {
        var uuid = Guid.NewGuid().ToString();

        // --- 本文中で参照される画像を URL→ローカルファイル名 に対応付け ---
        var imageBytes = new Dictionary<string, byte[]>();      // filename -> encoded bytes
        var urlToFile = new Dictionary<string, string>();        // source url -> filename
        if (options.IncludeInlineImages)
        {
            var byUrl = new Dictionary<string, ScrapedImage>();
            foreach (var img in allImages)
                byUrl.TryAdd(img.SourceUrl, img);

            var n = 0;
            foreach (var ep in episodes)
            {
                foreach (var url in ep.ImageUrls)
                {
                    if (urlToFile.ContainsKey(url)) continue;
                    if (!byUrl.TryGetValue(url, out var img)) continue;
                    n++;
                    var file = $"img{n:000}.jpg";
                    try
                    {
                        imageBytes[file] = ImageProcessor.EncodeForEpub(img.Data, options, isCover: false);
                        urlToFile[url] = file;
                    }
                    catch
                    {
                        n--; // エンコード失敗。本文側でタグを除去する。
                        progress?.Report($"挿絵のエンコードに失敗: {url}");
                    }
                }
            }
        }

        // --- 表紙 ---
        byte[]? coverBytes = null;
        if (cover != null)
        {
            try { coverBytes = ImageProcessor.EncodeForEpub(cover.Data, options, isCover: true, part, partCount); }
            catch { progress?.Report("表紙のエンコードに失敗しました。表紙なしで続行します。"); }
        }

        // --- 出力 ---
        if (File.Exists(outputPath)) File.Delete(outputPath);
        using (var fs = new FileStream(outputPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // mimetype は最初・無圧縮。
            WriteEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            WriteEntry(zip, "META-INF/container.xml", ContainerXml());
            WriteEntry(zip, "OEBPS/style.css", StyleCss(options));

            // エピソード XHTML
            var spine = new List<string>();       // idref
            var manifest = new List<string>();
            var navItems = new List<(int Index, string? Chapter, string Title, string File)>();

            for (var i = 0; i < episodes.Count; i++)
            {
                var ep = episodes[i];
                var file = $"text/p{i + 1:0000}.xhtml";
                var id = $"ep{i + 1:0000}";
                var xhtml = BuildEpisodeXhtml(ep, urlToFile, options, proofreading);
                WriteEntry(zip, $"OEBPS/{file}", xhtml);
                manifest.Add($"    <item id=\"{id}\" href=\"{file}\" media-type=\"application/xhtml+xml\"/>");
                spine.Add($"    <itemref idref=\"{id}\"/>");
                navItems.Add((ep.Ref.Index, ep.Ref.ChapterTitle, ep.Ref.Title, file));
                if (i % 25 == 0 || i == episodes.Count - 1)
                    progress?.Report($"XHTML生成 {i + 1}/{episodes.Count}");
            }

            // 表紙ページ
            var hasCover = coverBytes != null;
            if (hasCover)
            {
                WriteEntryBytes(zip, "OEBPS/images/cover.jpg", coverBytes!);
                WriteEntry(zip, "OEBPS/cover.xhtml", CoverXhtml(displayTitle));
            }

            // 挿絵
            foreach (var (file, bytes) in imageBytes)
                WriteEntryBytes(zip, $"OEBPS/images/{file}", bytes);

            // nav.xhtml
            WriteEntry(zip, "OEBPS/nav.xhtml", NavXhtml(meta, navItems));

            // content.opf
            WriteEntry(zip, "OEBPS/content.opf",
                ContentOpf(meta, displayTitle, uuid, hasCover, spine, manifest, imageBytes.Keys, options));
        }

        var size = new FileInfo(outputPath).Length;
        return new BuildResult
        {
            OutputPath = outputPath,
            EpisodeCount = episodes.Count,
            ImageCount = imageBytes.Count + (coverBytes != null ? 1 : 0),
            SizeBytes = size,
        };
    }

    private static string BuildEpisodeXhtml(
        EpisodeContent ep, Dictionary<string, string> urlToFile, EpubOptions opt,
        ProofreadingService? proofreading = null)
    {
        var bodyHtml = ep.BodyHtml;
        var forewordHtml = ep.ForewordHtml;
        var afterwordHtml = ep.AfterwordHtml;

        if (proofreading != null)
        {
            bodyHtml = proofreading.Apply(bodyHtml);
            if (!string.IsNullOrWhiteSpace(forewordHtml))
                forewordHtml = proofreading.Apply(forewordHtml!);
            if (!string.IsNullOrWhiteSpace(afterwordHtml))
                afterwordHtml = proofreading.Apply(afterwordHtml!);
        }

        var body = new StringBuilder();
        body.Append("<h1 class=\"ep-title\">").Append(XhtmlSanitizer.XmlEscape(ep.Ref.Title)).Append("</h1>\n");

        if (!string.IsNullOrWhiteSpace(forewordHtml))
            body.Append("<div class=\"note\">\n").Append(RewriteImages(forewordHtml!, urlToFile)).Append("</div>\n<hr/>\n");

        body.Append(RewriteImages(bodyHtml, urlToFile));

        if (!string.IsNullOrWhiteSpace(afterwordHtml))
            body.Append("\n<hr/>\n<div class=\"note\">\n").Append(RewriteImages(afterwordHtml!, urlToFile)).Append("</div>\n");

        return XhtmlDocument(ep.Ref.Title, body.ToString(), "style.css");
    }

    /// <summary>HTML のテキストノード部分（タグ外）にのみ変換関数を適用する。</summary>
    private static string ApplyToTextNodes(string html, Func<string, string> transform)
    {
        var sb = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            var tagStart = html.IndexOf('<', i);
            if (tagStart < 0) { sb.Append(transform(html[i..])); break; }
            if (tagStart > i) sb.Append(transform(html[i..tagStart]));
            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) { sb.Append(html[tagStart..]); break; }
            sb.Append(html[tagStart..(tagEnd + 1)]);
            i = tagEnd + 1;
        }
        return sb.ToString();
    }

    /// <summary>本文中の絶対URL img を、収録済みのローカル画像へ書き換える。未収録は除去。</summary>
    private static string RewriteImages(string html, Dictionary<string, string> urlToFile)
    {
        foreach (var (url, file) in urlToFile)
            html = html.Replace($"src=\"{XhtmlSanitizer.XmlEscape(url)}\"", $"src=\"../images/{file}\"");
        // 収録できなかった外部画像タグは除去（端末で読めないため）。
        html = LeftoverImgRegex().Replace(html, "");
        return html;
    }

    // ---- 各種XML生成 ----

    private static string XhtmlDocument(string title, string bodyInner, string cssRelFromText)
    {
        // text/ 配下からは ../style.css
        var css = cssRelFromText == "style.css" ? "../style.css" : cssRelFromText;
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"ja\" lang=\"ja\">\n" +
            "<head>\n<meta charset=\"UTF-8\"/>\n" +
            $"<title>{XhtmlSanitizer.XmlEscape(title)}</title>\n" +
            $"<link rel=\"stylesheet\" type=\"text/css\" href=\"{css}\"/>\n" +
            "</head>\n<body>\n" + bodyInner + "\n</body>\n</html>\n";
    }

    private static string CoverXhtml(string title)
    {
        var inner = "<div class=\"cover\"><img src=\"images/cover.jpg\" alt=\"" + XhtmlSanitizer.XmlEscape(title) + "\"/></div>";
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"ja\" lang=\"ja\">\n" +
            "<head>\n<meta charset=\"UTF-8\"/>\n<title>表紙</title>\n" +
            "<link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/>\n</head>\n" +
            "<body class=\"coverbody\">\n" + inner + "\n</body>\n</html>\n";
    }

    private static string NavXhtml(NovelMetadata meta, List<(int Index, string? Chapter, string Title, string File)> items)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n");
        sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"ja\" lang=\"ja\">\n");
        sb.Append("<head>\n<meta charset=\"UTF-8\"/>\n<title>目次</title>\n</head>\n<body>\n");
        sb.Append("<nav epub:type=\"toc\" id=\"toc\">\n<h1>目次</h1>\n<ol>\n");

        string? currentChapter = null;
        var chapterOpen = false;
        var inChapterListOpen = false;
        foreach (var it in items)
        {
            if (it.Chapter != currentChapter)
            {
                // 前の章のサブリストを閉じる
                if (inChapterListOpen) { sb.Append("</ol></li>\n"); inChapterListOpen = false; }
                else if (chapterOpen) { sb.Append("</li>\n"); chapterOpen = false; }

                currentChapter = it.Chapter;
                if (!string.IsNullOrWhiteSpace(currentChapter))
                {
                    sb.Append("<li><span>").Append(XhtmlSanitizer.XmlEscape(currentChapter!.Replace('　', ' '))).Append("</span>\n<ol>\n");
                    inChapterListOpen = true;
                }
            }
            sb.Append("<li><a href=\"").Append(it.File).Append("\">")
              .Append(XhtmlSanitizer.XmlEscape(it.Title.Replace('　', ' '))).Append("</a></li>\n");
        }
        if (inChapterListOpen) sb.Append("</ol></li>\n");

        sb.Append("</ol>\n</nav>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string ContentOpf(
        NovelMetadata meta, string displayTitle, string uuid, bool hasCover,
        List<string> spine, List<string> manifest, IEnumerable<string> imageFiles, EpubOptions opt)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\" xml:lang=\"ja\">\n");
        sb.Append("  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
        sb.Append($"    <dc:identifier id=\"bookid\">urn:uuid:{uuid}</dc:identifier>\n");
        sb.Append($"    <dc:title>{XhtmlSanitizer.XmlEscape(displayTitle)}</dc:title>\n");
        sb.Append($"    <dc:language>{opt.Language}</dc:language>\n");
        if (!string.IsNullOrWhiteSpace(meta.Author))
            sb.Append($"    <dc:creator>{XhtmlSanitizer.XmlEscape(meta.Author)}</dc:creator>\n");
        if (!string.IsNullOrWhiteSpace(meta.Description))
            sb.Append($"    <dc:description>{XhtmlSanitizer.XmlEscape(meta.Description)}</dc:description>\n");
        sb.Append($"    <dc:source>{XhtmlSanitizer.XmlEscape(meta.Url)}</dc:source>\n");
        sb.Append($"    <meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>\n");
        if (hasCover)
            sb.Append("    <meta name=\"cover\" content=\"cover-img\"/>\n");
        sb.Append("  </metadata>\n");

        sb.Append("  <manifest>\n");
        sb.Append("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        sb.Append("    <item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n");
        if (hasCover)
        {
            sb.Append("    <item id=\"cover-img\" href=\"images/cover.jpg\" media-type=\"image/jpeg\" properties=\"cover-image\"/>\n");
            sb.Append("    <item id=\"cover-page\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>\n");
        }
        foreach (var m in manifest) sb.Append(m).Append('\n');
        var k = 0;
        foreach (var f in imageFiles)
        {
            k++;
            sb.Append($"    <item id=\"imgf{k}\" href=\"images/{f}\" media-type=\"image/jpeg\"/>\n");
        }
        sb.Append("  </manifest>\n");

        var pageDir = opt.WritingMode == WritingMode.Vertical ? " page-progression-direction=\"rtl\"" : "";
        sb.Append($"  <spine{pageDir}>\n");
        if (hasCover) sb.Append("    <itemref idref=\"cover-page\"/>\n");
        foreach (var s in spine) sb.Append(s).Append('\n');
        sb.Append("  </spine>\n");
        sb.Append("</package>\n");
        return sb.ToString();
    }

    private static string ContainerXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
        "  <rootfiles>\n" +
        "    <rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>\n" +
        "  </rootfiles>\n</container>\n";

    private static string StyleCss(EpubOptions opt)
    {
        var vertical = opt.WritingMode == WritingMode.Vertical;
        var sb = new StringBuilder();
        sb.Append("html{");
        if (vertical) sb.Append("-epub-writing-mode:vertical-rl;writing-mode:vertical-rl;");
        sb.Append("}\n");
        sb.Append($"body{{margin:0;padding:1em;line-height:1.8;font-size:{opt.BaseFontPercent}%;");
        if (vertical) sb.Append("writing-mode:vertical-rl;");
        sb.Append("}}\n");
        sb.Append("h1.ep-title{font-size:1.3em;line-height:1.4;margin:0 0 1.2em;}\n");
        sb.Append("p{margin:0;text-indent:1em;}\n");
        sb.Append("hr{border:0;border-top:1px solid #888;margin:1em 0;}\n");
        sb.Append("img{max-width:100%;max-height:100%;height:auto;}\n");
        sb.Append("rt{font-size:0.6em;}\n");
        sb.Append(".note{font-size:0.9em;color:#333;}\n");
        sb.Append(".cover{margin:0;text-align:center;}\n");
        sb.Append(".cover img{max-width:100%;max-height:100%;}\n");
        sb.Append("body.coverbody{padding:0;}\n");
        return sb.ToString();
    }

    // ---- zip helpers ----

    private static void WriteEntry(ZipArchive zip, string path, string content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteEntryBytes(ZipArchive zip, string path, byte[] content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var s = entry.Open();
        s.Write(content, 0, content.Length);
    }

    /// <summary>
    /// 既存EPUBを縦書き設定・目次全角スペース修正・テキスト置換でインプレースパッチする。
    /// 再ダウンロード不要。style.css / content.opf / nav.xhtml と本文 XHTML を差し替え。
    /// </summary>
    public static void PatchVertical(string epubPath, EpubOptions opt,
        IReadOnlyList<(string From, string To)>? textReplacements = null)
    {
        var tmp = epubPath + ".tmp";
        try
        {
            using (var srcFs = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var srcZip = new ZipArchive(srcFs, ZipArchiveMode.Read))
            using (var dstFs = new FileStream(tmp, FileMode.Create))
            using (var dstZip = new ZipArchive(dstFs, ZipArchiveMode.Create))
            {
                foreach (var entry in srcZip.Entries)
                {
                    var name = entry.FullName;
                    var level = name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;

                    using var srcStream = entry.Open();
                    using var ms = new MemoryStream();
                    srcStream.CopyTo(ms);
                    var bytes = ms.ToArray();

                    string? patched = null;
                    if (name == "OEBPS/style.css")
                    {
                        patched = StyleCss(opt);
                    }
                    else if (name == "OEBPS/content.opf")
                    {
                        var opf = Encoding.UTF8.GetString(bytes);
                        if (!opf.Contains("page-progression-direction"))
                            opf = opf.Replace("<spine>", "<spine page-progression-direction=\"rtl\">");
                        patched = opf;
                    }
                    else if (name == "OEBPS/nav.xhtml")
                    {
                        patched = Encoding.UTF8.GetString(bytes).Replace("　", " ");
                    }
                    else if (name.StartsWith("OEBPS/text/") && name.EndsWith(".xhtml")
                             && textReplacements is { Count: > 0 })
                    {
                        var xhtml = Encoding.UTF8.GetString(bytes);
                        foreach (var (from, to) in textReplacements)
                            xhtml = ApplyToTextNodes(xhtml, s => s.Replace(from, to, StringComparison.Ordinal));
                        patched = xhtml;
                    }

                    if (patched != null)
                        WriteEntry(dstZip, name, patched, level);
                    else
                        WriteEntryBytes(dstZip, name, bytes, level);
                }
            }
            File.Move(tmp, epubPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }
}
