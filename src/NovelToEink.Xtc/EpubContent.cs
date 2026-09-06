using System.IO.Compression;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace NovelToEink.Xtc;

/// <summary>本文中に現れる要素の種類。</summary>
public enum EpubNodeKind
{
    /// <summary>文字列。</summary>
    Text,

    /// <summary>ルビ（親文字 + 読み）。</summary>
    Ruby,

    /// <summary>画像。</summary>
    Image,

    /// <summary>ブロック要素の区切り（改行）。</summary>
    LineBreak,
}

/// <summary>本文を先頭から順に並べたときの 1 要素。</summary>
public sealed class EpubNode
{
    /// <summary>要素の種類。</summary>
    public EpubNodeKind Kind { get; init; }

    /// <summary>本文文字列、またはルビの親文字。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>ルビの読み。</summary>
    public string Ruby { get; init; } = string.Empty;

    /// <summary>画像のバイト列。</summary>
    public byte[]? ImageData { get; init; }
}

/// <summary>EPUB のメタデータ。</summary>
public sealed class EpubInfo
{
    /// <summary>書名。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>著者。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>言語コード。</summary>
    public string Language { get; set; } = "ja";
}

/// <summary>
/// EPUB を読み、spine 順に本文を平坦な <see cref="EpubNode"/> 列へ展開する。
/// 既存の EPUBReader と違い、挿絵とルビが本文中の出現位置を保ったまま残る。
/// </summary>
public static class EpubContent
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "section", "header", "li", "br", "blockquote",
    };

    /// <summary>EPUB を読み、章ごとのノード列を返す。</summary>
    public static (EpubInfo Info, List<List<EpubNode>> Chapters) Read(string epubPath)
    {
        using var zip = ZipFile.OpenRead(epubPath);

        var opfPath = ResolveOpfPath(zip);
        var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        var opf = XDocument.Parse(ReadAllText(zip, opfPath));

        var info = ReadMetadata(opf);
        var manifest = opf.Descendants(Opf + "manifest").Elements(Opf + "item")
            .Select(e => (
                Id: (string?)e.Attribute("id") ?? string.Empty,
                Href: (string?)e.Attribute("href") ?? string.Empty,
                MediaType: (string?)e.Attribute("media-type") ?? string.Empty))
            .Where(i => i.Id.Length > 0)
            .ToDictionary(i => i.Id, i => i, StringComparer.Ordinal);

        var chapters = new List<List<EpubNode>>();
        foreach (var itemRef in opf.Descendants(Opf + "spine").Elements(Opf + "itemref"))
        {
            var idref = (string?)itemRef.Attribute("idref");
            if (idref is null || !manifest.TryGetValue(idref, out var item)) continue;
            if (!item.MediaType.Contains("html", StringComparison.OrdinalIgnoreCase)) continue;

            var entryPath = CombineZipPath(opfDir, item.Href);
            var html = ReadAllText(zip, entryPath);
            if (html.Length == 0) continue;

            var nodes = new List<EpubNode>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
            var chapterDir = Path.GetDirectoryName(entryPath)?.Replace('\\', '/') ?? string.Empty;
            Walk(body, nodes, zip, chapterDir);

            if (nodes.Count > 0) chapters.Add(nodes);
        }

        return (info, chapters);
    }

    private static void Walk(HtmlNode node, List<EpubNode> output, ZipArchive zip, string baseDir)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Text:
                {
                    var text = HtmlEntity.DeEntitize(child.InnerText).Replace("\r", "").Replace("\n", "").Trim();
                    if (text.Length > 0)
                        output.Add(new EpubNode { Kind = EpubNodeKind.Text, Text = text });
                    break;
                }

                case HtmlNodeType.Element:
                {
                    var name = child.Name.ToLowerInvariant();

                    if (name is "img" or "image")
                    {
                        var src = FirstAttribute(child, "src", "xlink:href", "href");
                        var data = src is null ? null : ReadImage(zip, baseDir, src);
                        if (data is not null)
                            output.Add(new EpubNode { Kind = EpubNodeKind.Image, ImageData = data });
                        break;
                    }

                    if (name == "ruby")
                    {
                        AppendRuby(child, output);
                        break;
                    }

                    if (name is "script" or "style") break;

                    Walk(child, output, zip, baseDir);

                    if (BlockElements.Contains(name))
                        output.Add(new EpubNode { Kind = EpubNodeKind.LineBreak });
                    break;
                }
            }
        }
    }

    private static string? FirstAttribute(HtmlNode node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = node.GetAttributeValue(name, string.Empty);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static void AppendRuby(HtmlNode ruby, List<EpubNode> output)
    {
        // <rt> が読み、それ以外（<rb> や素のテキスト）が親文字。<rp> は読み上げ用の括弧なので捨てる。
        var readingNodes = ruby.SelectNodes(".//rt");
        var reading = readingNodes is null ? string.Empty : string.Concat(readingNodes.Select(n => n.InnerText));
        var baseText = string.Concat(ruby.ChildNodes
            .Where(n => n.Name is not ("rt" or "rp"))
            .Select(n => n.InnerText));

        baseText = HtmlEntity.DeEntitize(baseText).Replace("\n", "").Trim();
        reading = HtmlEntity.DeEntitize(reading).Replace("\n", "").Trim();

        if (baseText.Length == 0) return;
        output.Add(new EpubNode { Kind = EpubNodeKind.Ruby, Text = baseText, Ruby = reading });
    }

    private static EpubInfo ReadMetadata(XDocument opf)
    {
        var metadata = opf.Descendants(Opf + "metadata").FirstOrDefault();
        return new EpubInfo
        {
            Title = metadata?.Elements(Dc + "title").FirstOrDefault()?.Value.Trim() ?? string.Empty,
            Author = metadata?.Elements(Dc + "creator").FirstOrDefault()?.Value.Trim() ?? string.Empty,
            Language = metadata?.Elements(Dc + "language").FirstOrDefault()?.Value.Trim() ?? "ja",
        };
    }

    private static string ResolveOpfPath(ZipArchive zip)
    {
        var containerXml = ReadAllText(zip, "META-INF/container.xml");
        if (containerXml.Length > 0)
        {
            var path = XDocument.Parse(containerXml)
                .Descendants(Container + "rootfile")
                .Select(e => (string?)e.Attribute("full-path"))
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (path is not null) return path;
        }

        var fallback = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
        return fallback?.FullName ?? throw new InvalidDataException("EPUB に OPF が見つかりません。");
    }

    private static byte[]? ReadImage(ZipArchive zip, string baseDir, string src)
    {
        var decoded = Uri.UnescapeDataString(src.Split('#')[0]);
        var entry = FindEntry(zip, CombineZipPath(baseDir, decoded));

        // 相対パスが解決できない EPUB もあるため、ファイル名一致でも探す。
        if (entry is null)
        {
            var fileName = decoded.Split('/').Last();
            entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }

        if (entry is null) return null;

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadAllText(ZipArchive zip, string path)
    {
        var entry = FindEntry(zip, path);
        if (entry is null) return string.Empty;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return zip.GetEntry(normalized)
               ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string CombineZipPath(string baseDir, string relative)
    {
        if (relative.StartsWith('/')) return relative.TrimStart('/');
        var combined = string.IsNullOrEmpty(baseDir) ? relative : baseDir + "/" + relative;

        var parts = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }
        return string.Join('/', parts);
    }
}
