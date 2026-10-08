using System;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NovelToEink.XtcConverter;

namespace NovelToEink.Core;

/// <summary>
/// Xteink X3 のような非力な E-Ink 端末向けに「軽い」EPUB3 を生成する。
/// ・スタイルシートは最小限（巨大CSSのRAM展開で落ちる端末対策）
/// ・1エピソード=1XHTML（1ファイルあたりのDOMを小さく保つ）
/// ・画像はJPEG（WebP不可の端末向け）
/// </summary>
public static partial class EpubBuilder
{
    [GeneratedRegex(@"""<img\b[^>]*\bsrc=""https?:[^""]*""[^>]*/>""", RegexOptions.IgnoreCase)]
    private static partial Regex LeftoverImgRegex();

    public sealed class BuildResult
    {
        public required string OutputPath { get; init; }
        public required int EpisodeCount { get; init; }
        public required int ImageCount { get; init; }
        public required long SizeBytes { get; init; }
    }

    /// <summary>1作品を（必要なら複数ファイルに分割して）生成する。</summary>
    /// <param name="novel">小説データ</param>
    /// <param name="cover">表紙画像。null なら表紙ページ無し。</param>
    /// <param name="options">EPUB生成オプション</param>
    /// <param name="outputDir">出力フォルダ</param>
    /// <param name="nameTemplate">ファイル名テンプレート（{title}/{author}/{part}）</param>
    /// <param name="progress">進行状況報告</param>
    /// <param name="proofreading">テキスト校正サービス（null なら校正しない）</param>
    /// <returns>生成結果</returns>
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
            var result = BuildOne(meta, subset, novel.Images, cover, options, outPath, displayTitle, progress, part, partCount, proofreading);
            
            // Generate XTC if requested
            if (options.GenerateXtc)
            {
                XtcConversionUtility.GenerateXtcFile(result.OutputPath, options);
            }
            
            results.Add(result);
        }
        return results;
    }

    /// <summary>1作品を1ファイルにまとめて生成する。</summary>
    /// <param name="novel">小説データ</param>
    /// <param name="cover">表紙画像。null なら表紙ページ無し。</param>
    /// <param name="options">EPUB生成オプション</param>
    /// <param name="outputPath">出力ファイルパス</param>
    /// <param name="progress">進行状況報告</param>
    /// <param name="proofreading">テキスト校正サービス（null なら校正しない）</param>
    /// <returns>生成結果</returns>
    public static BuildResult Build(
        NovelDownload novel,
        ScrapedImage? cover,
        EpubOptions options,
        string outputPath,
        IProgress<string>? progress = null,
        ProofreadingService? proofreading = null)
    {
        var result = BuildOne(novel.Metadata, novel.Episodes, novel.Images, cover, options, outputPath, novel.Metadata.Title, progress, 0, 0, proofreading);
        
        // Generate XTC if requested
        if (options.GenerateXtc)
        {
            XtcConversionUtility.GenerateXtcFile(result.OutputPath, options);
        }
        
        return result;
    }

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
                    catch (Exception ex)
                    {
                        CoreLog.Warn($"挿絵のエンコードに失敗: {url}", ex);
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
            catch (Exception ex)
            {
                // 画像デコード側の例外の種類は多岐にわたるため絞らず、表紙なしで続行する（原因はログに残す）
                CoreLog.Warn("表紙のエンコードに失敗", ex);
                progress?.Report("表紙のエンコードに失敗しました。表紙なしで続行します。");
            }
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

    // ---- XML namespace constants ----
    private static readonly XNamespace XhtmlNs = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace EpubNs = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";

    // ---- 各種XML生成（XElement ベース） ----

    /// <summary>EPUB3 XHTML ドキュメント全体を生成する。</summary>
    private static string XhtmlDocument(string title, string bodyInner, string cssRelFromText)
    {
        var css = cssRelFromText == "style.css" ? "../style.css" : cssRelFromText;

        var html = new XElement(XhtmlNs + "html",
            new XAttribute("xmlns", XhtmlNs),
            new XAttribute(XNamespace.Xmlns + "epub", EpubNs),
            new XAttribute(XNamespace.Xmlns + "xml", "http://www.w3.org/XML/1998/namespace"),
            new XAttribute("lang", "ja"),
            new XAttribute(XNamespace.Get("http://www.w3.org/XML/1998/namespace") + "lang", "ja"),
            new XElement(XhtmlNs + "head",
                new XElement(XhtmlNs + "meta",
                    new XAttribute("charset", "UTF-8")),
                new XElement(XhtmlNs + "title", XhtmlSanitizer.XmlEscape(title)),
                new XElement(XhtmlNs + "link",
                    new XAttribute("rel", "stylesheet"),
                    new XAttribute("type", "text/css"),
                    new XAttribute("href", css))
            ),
            new XElement(XhtmlNs + "body",
                bodyInner)
        );

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("html", null, null, null),
            html);
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>表紙ページ（cover.xhtml）を生成する。</summary>
    private static string CoverXhtml(string title)
    {
        var inner = new XElement(XhtmlNs + "div",
            new XAttribute("class", "cover"),
            new XElement(XhtmlNs + "img",
                new XAttribute("src", "images/cover.jpg"),
                new XAttribute("alt", XhtmlSanitizer.XmlEscape(title))));

        var html = new XElement(XhtmlNs + "html",
            new XAttribute("xmlns", XhtmlNs),
            new XAttribute(XNamespace.Xmlns + "epub", EpubNs),
            new XAttribute(XNamespace.Xmlns + "xml", "http://www.w3.org/XML/1998/namespace"),
            new XAttribute("lang", "ja"),
            new XAttribute(XNamespace.Get("http://www.w3.org/XML/1998/namespace") + "lang", "ja"),
            new XElement(XhtmlNs + "head",
                new XElement(XhtmlNs + "meta",
                    new XAttribute("charset", "UTF-8")),
                new XElement(XhtmlNs + "title", "表紙"),
                new XElement(XhtmlNs + "link",
                    new XAttribute("rel", "stylesheet"),
                    new XAttribute("type", "text/css"),
                    new XAttribute("href", "style.css"))),
            new XElement(XhtmlNs + "body",
                new XAttribute("class", "coverbody"),
                inner));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("html", null, null, null),
            html);
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>nav.xhtml（EPUB3 NCX 代替）を生成する。</summary>
    private static string NavXhtml(NovelMetadata meta, List<(int Index, string? Chapter, string Title, string File)> items)
    {
        var navItems = new XElement(XhtmlNs + "nav",
            new XAttribute(EpubNs + "type", "toc"),
            new XAttribute("id", "toc"),
            new XElement(XhtmlNs + "h1", "目次"),
            BuildNavList(items));

        var html = new XElement(XhtmlNs + "html",
            new XAttribute("xmlns", XhtmlNs),
            new XAttribute(XNamespace.Xmlns + "epub", EpubNs),
            new XAttribute(XNamespace.Xmlns + "xml", "http://www.w3.org/XML/1998/namespace"),
            new XAttribute("lang", "ja"),
            new XAttribute(XNamespace.Get("http://www.w3.org/XML/1998/namespace") + "lang", "ja"),
            new XElement(XhtmlNs + "head",
                new XElement(XhtmlNs + "meta",
                    new XAttribute("charset", "UTF-8")),
                new XElement(XhtmlNs + "title", "目次")),
            new XElement(XhtmlNs + "body", navItems));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("html", null, null, null),
            html);
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>目次リストを構築する。章（Chapter）が変わると <li><span>...</span><ol>...</ol></li> を生成。</summary>
    private static XElement BuildNavList(List<(int Index, string? Chapter, string Title, string File)> items)
    {
        var ol = new XElement(XhtmlNs + "ol");
        string? currentChapter = null;
        XElement? currentChapterOl = null;

        foreach (var it in items)
        {
            if (it.Chapter != currentChapter)
            {
                currentChapter = it.Chapter;
                if (!string.IsNullOrWhiteSpace(currentChapter))
                {
                    var chapterSpan = new XElement(XhtmlNs + "span",
                        XhtmlSanitizer.XmlEscape(currentChapter!.Replace(' ', ' ')));
                    currentChapterOl = new XElement(XhtmlNs + "ol");
                    ol.Add(new XElement(XhtmlNs + "li",
                        chapterSpan,
                        currentChapterOl));
                }
            }

            var li = new XElement(XhtmlNs + "li",
                new XElement(XhtmlNs + "a",
                    new XAttribute("href", it.File),
                    XhtmlSanitizer.XmlEscape(it.Title.Replace(' ', ' '))));

            (currentChapterOl ?? ol).Add(li);
        }

        return ol;
    }

    /// <summary>content.opf（EPUB3 パッケージ文書）を生成する。</summary>
    private static string ContentOpf(
        NovelMetadata meta, string displayTitle, string uuid, bool hasCover,
        List<string> spine, List<string> manifest, IEnumerable<string> imageFiles, EpubOptions opt)
    {
        var metadata = new XElement(DcNs + "metadata",
            new XAttribute(XNamespace.Xmlns + "dc", DcNs),
            new XElement(DcNs + "identifier",
                new XAttribute("id", "bookid"),
                $"urn:uuid:{uuid}"),
            new XElement(DcNs + "title", XhtmlSanitizer.XmlEscape(displayTitle)),
            new XElement(DcNs + "language", opt.Language),
            meta.Author is not null and not "" ? new XElement(DcNs + "creator", XhtmlSanitizer.XmlEscape(meta.Author)) : null,
            meta.Description is not null and not "" ? new XElement(DcNs + "description", XhtmlSanitizer.XmlEscape(meta.Description)) : null,
            new XElement(DcNs + "source", XhtmlSanitizer.XmlEscape(meta.Url)),
            new XElement(XhtmlNs + "meta",
                new XAttribute("property", "dcterms:modified"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            hasCover ? new XElement(XhtmlNs + "meta",
                new XAttribute("name", "cover"),
                new XAttribute("content", "cover-img")) : null);

        var manifestItems = new List<XElement>
        {
            new XElement(XhtmlNs + "item",
                new XAttribute("id", "nav"),
                new XAttribute("href", "nav.xhtml"),
                new XAttribute("media-type", "application/xhtml+xml"),
                new XAttribute("properties", "nav")),
            new XElement(XhtmlNs + "item",
                new XAttribute("id", "css"),
                new XAttribute("href", "style.css"),
                new XAttribute("media-type", "text/css"))
        };

        if (hasCover)
        {
            manifestItems.Add(new XElement(XhtmlNs + "item",
                new XAttribute("id", "cover-img"),
                new XAttribute("href", "images/cover.jpg"),
                new XAttribute("media-type", "image/jpeg"),
                new XAttribute("properties", "cover-image")));
            manifestItems.Add(new XElement(XhtmlNs + "item",
                new XAttribute("id", "cover-page"),
                new XAttribute("href", "cover.xhtml"),
                new XAttribute("media-type", "application/xhtml+xml")));
        }

        foreach (var m in manifest)
        {
            var el = XElement.Parse(m);
            if (el.Name.Namespace == XNamespace.None)
                el.Name = XhtmlNs + el.Name.LocalName;
            manifestItems.Add(el);
        }

        var k = 0;
        foreach (var f in imageFiles)
        {
            k++;
            manifestItems.Add(new XElement(XhtmlNs + "item",
                new XAttribute("id", $"imgf{k}"),
                new XAttribute("href", $"images/{f}"),
                new XAttribute("media-type", "image/jpeg")));
        }

        var manifestEl = new XElement(XhtmlNs + "manifest",
            manifestItems);

        var pageDir = opt.WritingMode == WritingMode.Vertical ? "rtl" : null;
        var spineEl = new XElement(XhtmlNs + "spine",
            pageDir is not null ? new XAttribute("page-progression-direction", pageDir) : null,
            hasCover ? new XElement(XhtmlNs + "itemref",
                new XAttribute("idref", "cover-page")) : null);

        foreach (var s in spine)
        {
            var el = XElement.Parse(s);
            if (el.Name.Namespace == XNamespace.None)
                el.Name = XhtmlNs + el.Name.LocalName;
            spineEl.Add(el);
        }

        var package = new XElement(XhtmlNs + "package",
            new XAttribute("xmlns", XhtmlNs),
            new XAttribute("version", "3.0"),
            new XAttribute("unique-identifier", "bookid"),
            new XAttribute(XNamespace.Xmlns + "dc", DcNs),
            new XAttribute("lang", "ja"),
            metadata,
            manifestEl,
            spineEl);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            package);
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>META-INF/container.xml を生成する。</summary>
    private static string ContainerXml()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ContainerNs + "container",
                new XAttribute("version", "1.0"),
                new XAttribute("xmlns", ContainerNs),
                new XElement(ContainerNs + "rootfiles",
                    new XElement(ContainerNs + "rootfile",
                        new XAttribute("full-path", "OEBPS/content.opf"),
                        new XAttribute("media-type", "application/oebps-package+xml")))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }



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