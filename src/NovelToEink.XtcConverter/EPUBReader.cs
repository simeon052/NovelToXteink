using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace NovelToEink.XtcConverter
{
    public class RubySegment
    {
        public string Term { get; set; }
        public string Reading { get; set; }
    }

    public class TextBlock
    {
        /// <summary>"para" | "title" | "sub" | "caption" | "empty"</summary>
        public string Kind { get; set; } = "para";
        public string Text { get; set; } = string.Empty;
        public List<RubySegment> Rubies { get; set; } = new List<RubySegment>();
    }

    public class XtcImage
    {
        public string MediaType { get; set; } = "image/jpeg";
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Name { get; set; } = string.Empty;
    }

    public class XtcSection
    {
        public string Href { get; set; } = string.Empty;
        public List<TextBlock> Blocks { get; set; } = new List<TextBlock>();
        public List<XtcImage> Images { get; set; } = new List<XtcImage>();
    }

    public class EPUBMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public string Language { get; set; } = "ja";
        public string Publisher { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }

    public static class EPUBReader
    {
        public const int DefaultMaxCharsPerPage = 1800;

        public static (EPUBMetadata metadata, List<XtcSection> sections) Read(string epubPath)
        {
            using var zip = ZipFile.OpenRead(epubPath);

            string opfPath = FindRootFile(zip);
            if (opfPath == null)
                throw new InvalidDataException("META-INF/container.xml rootfile not found in EPUB");

            var opf = ParsePackage(zip, opfPath);

            var manifest = new Dictionary<string, string>();
            var manifestOrder = new List<string>();
            foreach (var it in opf.Items)
            {
                if (!IsXhtml(it.MediaType))
                    continue;
                manifest[it.Id] = it.Href;
                manifestOrder.Add(it.Href);
            }

            var sections = new List<XtcSection>();
            var processed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var href in manifestOrder)
            {
                if (!manifest.TryGetValue(href, out var resolved) || !resolved.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!processed.Add(resolved))
                    continue;

                var section = ReadXhtml(zip, resolved, href);
                if (section.Blocks.Count == 0 && section.Images.Count == 0)
                    continue;
                sections.Add(section);
            }

            return (opf.Metadata, sections);
        }

        private static bool IsXhtml(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType))
                return false;
            return mediaType.ToLowerInvariant().Contains("xhtml");
        }

        private static string FindRootFile(ZipArchive zip)
        {
            var entry = zip.GetEntry("META-INF/container.xml")
                        ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals("META-INF/container.xml", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            var rootfiles = doc.Root.Elements().First(e => e.Name.LocalName == "rootfiles");
            var rootfile = rootfiles.Elements().First(e => e.Name.LocalName == "rootfile");
            return (string)rootfile.Attribute("full-path");
        }

        private static (EPUBMetadata Metadata, List<ManifestItem> Items) ParsePackage(ZipArchive zip, string opfPath)
        {
            var entry = zip.GetEntry(opfPath)
                        ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(opfPath, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new InvalidDataException($"Package file not found in EPUB: {opfPath}");
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            var root = doc.Root;

            var metadata = new EPUBMetadata();

            var md = root.Elements().First(e => e.Name.LocalName == "metadata");
            metadata.Title = GetXChild(md, "title");
            metadata.Creator = GetXChild(md, "creator");
            metadata.Language = GetXChild(md, "language") ?? "ja";
            metadata.Publisher = GetXChild(md, "publisher");
            var id = GetXChild(md, "identifier");
            if (id != null)
                metadata.Id = id;
            if (string.IsNullOrEmpty(metadata.Language))
            {
                var xmlLang = root.Attribute("xml:lang")?.Value;
                if (!string.IsNullOrEmpty(xmlLang))
                    metadata.Language = xmlLang;
            }

            var manifest = root.Elements().First(e => e.Name.LocalName == "manifest");
            var items = new List<ManifestItem>();
            foreach (var it in manifest.Elements())
            {
                if (it.Name.LocalName != "item")
                    continue;
                var mi = new ManifestItem
                {
                    Id = (string)it.Attribute("id"),
                    Href = (string)it.Attribute("href"),
                    MediaType = (string)it.Attribute("media-type"),
                };
                items.Add(mi);
            }

            return (metadata, items);
        }

        private static string GetXChild(XElement parent, string localName)
        {
            var el = parent.Nodes().OfType<XElement>().FirstOrDefault(e => e.Name.LocalName == localName);
            return el?.Value;
        }

        private static XtcSection ReadXhtml(ZipArchive zip, string resolved, string href)
        {
            var section = new XtcSection { Href = href };

            var entry = zip.GetEntry(resolved)
                        ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(resolved, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return section;
            using var s = entry.Open();
            var doc = new HtmlDocument();
            doc.OptionFixNestedTags = true;
            doc.Load(s, Encoding.UTF8, true);

            var body = doc.DocumentNode.SelectNodes("//body");
            HtmlNode bodyNode = body == null || body.Count == 0 ? null : body.First();
            if (bodyNode == null)
                return section;

            WalkNode(zip, bodyNode, section);
            return section;
        }

        private static void WalkNode(ZipArchive zip, HtmlNode node, XtcSection section)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is HtmlTextNode tn)
                {
                    var text = tn.Text.Trim();
                    if (text.Length > 0)
                    {
                        var block = new TextBlock { Kind = "para", Text = ApplyVerticalWriting(text) };
                        section.Blocks.Add(block);
                    }
                }
                else if (child is HtmlNode cn)
                {
                    switch (cn.Name)
                    {
                        case "h1":
                        case "h2":
                        case "h3":
                        case "h4":
                            AddTitleBlock(cn, section);
                            break;
                        case "p":
                            AddParaBlock(cn, section);
                            break;
                        case "img":
                            var img = ReadImage(zip, cn);
                            if (img != null)
                                section.Images.Add(img);
                            break;
                        case "hr":
                            section.Blocks.Add(new TextBlock { Kind = "empty", Text = "───────" });
                            break;
                        default:
                            WalkNode(zip, cn, section);
                            break;
                    }
                }
            }
        }

        private static void AddTitleBlock(HtmlNode node, XtcSection section)
        {
            var text = CollapseWhitespace(node.InnerText);
            if (text.Length == 0)
                return;
            section.Blocks.Add(new TextBlock { Kind = "title", Text = ApplyVerticalWriting(text) });
        }

        private static void AddParaBlock(HtmlNode node, XtcSection section)
        {
            var lines = ExtractLines(node);
            if (lines.Count == 0)
                return;
            var block = new TextBlock { Kind = "para", Text = string.Join("\n", lines.Select(ApplyVerticalWriting)) };
            section.Blocks.Add(block);
        }

        private static List<string> ExtractLines(HtmlNode node)
        {
            var lines = new List<string>();
            var current = new StringBuilder();
            void Flush()
            {
                var t = CollapseWhitespace(current.ToString());
                current.Clear();
                if (t.Length > 0)
                    lines.Add(t);
            }

            foreach (var child in node.ChildNodes)
            {
                if (child is HtmlTextNode tn)
                {
                    current.Append(tn.Text);
                }
                else if (child is HtmlNode cn)
                {
                    switch (cn.Name)
                    {
                        case "br":
                        case "/":
                            Flush();
                            current.Append("\n");
                            break;
                        case "ruby":
                            current.Append(RubyInline(cn));
                            break;
                        case "img":
                            current.Append("〔図〕");
                            break;
                        case "rt":
                        case "rp":
                            break;
                        default:
                            current.Append(cn.InnerText);
                            break;
                    }
                }
            }
            Flush();
            return lines;
        }

        private static string RubyInline(HtmlNode ruby)
        {
            string term = string.Empty;
            string reading = string.Empty;
            foreach (var child in ruby.ChildNodes)
            {
                if (child is HtmlTextNode ntn)
                {
                    var t = ntn.Text.Trim();
                    if (t.Length > 0) term += t;
                }
                else if (child is HtmlNode ncn)
                {
                    switch (ncn.Name)
                    {
                        case "rt":
                            reading = ncn.InnerText;
                            break;
                        case "rp":
                            break;
                        case "span":
                            if (IsRubySpan(ncn))
                                reading = reading.Length == 0 ? ncn.InnerText : reading;
                            else
                                term += ncn.InnerText;
                            break;
                        default:
                            term += ncn.InnerText;
                            break;
                    }
                }
            }
            return reading.Length == 0 ? term : $"{term}（{reading}）";
        }

        private static bool IsRubySpan(HtmlNode span)
        {
            var cls = span.GetAttributeValue("class", string.Empty);
            return cls.Split(' ').Any(c => c.Equals("rt", StringComparison.OrdinalIgnoreCase)
                                             || c.Equals("r", StringComparison.OrdinalIgnoreCase));
        }

        private static XtcImage ReadImage(ZipArchive zip, HtmlNode imgNode)
        {
            var src = imgNode.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrEmpty(src))
                src = imgNode.GetAttributeValue("data-src", string.Empty);
            if (string.IsNullOrWhiteSpace(src))
                return null;

            var img = FindImageBytes(zip, src);
            if (img == null)
                return null;

            return new XtcImage { MediaType = GuessMediaType(src), Data = img, Name = System.IO.Path.GetFileName(src) };
        }

        private static byte[] FindImageBytes(ZipArchive zip, string src)
        {
            if (src.StartsWith("data:", StringComparison.Ordinal))
                return null;

            var candidates = new List<string>();
            var dir = src.Substring(0, src.LastIndexOf('/') + 1);
            candidates.Add(src);
            candidates.Add(dir + src);
            candidates.Add(Uri.EscapeDataString(src));
            candidates.Add(Uri.EscapeDataString(dir + src));
            candidates.Add(Uri.UnescapeDataString(src));
            candidates.Add(Uri.UnescapeDataString(dir + src));

            foreach (var c in candidates)
            {
                var e = zip.GetEntry(c) ?? zip.Entries.FirstOrDefault(x => x.FullName.Equals(c, StringComparison.OrdinalIgnoreCase));
                if (e != null)
                    return ReadAll(e);
            }

            var fname = src.Split('/').Last();
            var byName = zip.Entries.FirstOrDefault(x => x.FullName.Split('/').Last().Equals(fname, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return ReadAll(byName);

            return null;
        }

        private static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using var ms = new MemoryStream();
            using var es = entry.Open();
            es.CopyTo(ms);
            return ms.ToArray();
        }

        private static string GuessMediaType(string src)
        {
            var ext = System.IO.Path.GetExtension(src).ToLowerInvariant();
            switch (ext)
            {
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                default: return "image/jpeg";
            }
        }

        private static string CollapseWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            foreach (var ch in s)
            {
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                {
                    if (!prevSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        prevSpace = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    prevSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private static string ApplyVerticalWriting(string s)
        {
            return s.Replace('\u301C', '\u4E62')
                    .Replace('\uFF5E', '\u4E62')
                    .Replace('\u2014', '\u4E62')
                    .Replace('\u2015', '\u4E62')
                    .Replace('\u2013', '\u4E62');
        }
    }

    internal class ManifestItem
    {
        public string Id { get; set; }
        public string Href { get; set; }
        public string MediaType { get; set; }
    }
}
