using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SixLabors.ImageSharp;

namespace NovelToEink.XtcConverter
{
    /// <summary>
    /// XTC Converter for X4 Pro devices (480x800).
    /// Parses an EPUB and generates one XTC page per rendered screen.
    /// </summary>
    public class X4ProXtcConverter
    {
        /// <summary>Max text characters (including newlines) per generated page.</summary>
        private const int MaxCharsPerPage = 130;

        private readonly XtcOptions _options;

        public X4ProXtcConverter(XtcOptions options)
        {
            _options = options ?? new XtcOptions();
        }

        /// <summary>Converts an EPUB file to XTC format for X4 Pro.</summary>
        public void ConvertEpubToXtc(string epubPath, string outputPath)
        {
            ConvertEpubToXtc(epubPath, outputPath, _options);
        }

        /// <summary>Converts an EPUB file to XTC format for X4 Pro with custom options.</summary>
        public void ConvertEpubToXtc(string epubPath, string outputPath, XtcOptions options)
        {
            if (string.IsNullOrEmpty(epubPath))
                throw new ArgumentException("EPUB path cannot be null or empty", nameof(epubPath));
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
            if (!File.Exists(epubPath))
                throw new FileNotFoundException($"EPUB file not found: {epubPath}");

            if (options.Resolution.Width != 480 || options.Resolution.Height != 800)
            {
                options.Resolution = (480, 800);
                Console.WriteLine("Warning: X4 Pro requires 480x800 resolution. Setting to default.");
            }

            Console.WriteLine($"Converting EPUB to XTC: {epubPath} -> {outputPath}");
            Console.WriteLine($"Resolution: {options.Resolution.Width}x{options.Resolution.Height}");
            Console.WriteLine($"Vertical Writing: {options.EnableVerticalWriting}");
            Console.WriteLine($"Font: {options.FontFile ?? "Default"}");

            CreateXtcFile(epubPath, outputPath, options);
        }

        /// <summary>Asynchronously converts an EPUB file to XTC format for X4 Pro.</summary>
        public async Task ConvertAsync(string epubPath, string outputPath, XtcOptions options)
        {
            if (string.IsNullOrEmpty(epubPath))
                throw new ArgumentException("EPUB path cannot be null or empty", nameof(epubPath));
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
            if (!File.Exists(epubPath))
                throw new FileNotFoundException($"EPUB file not found: {epubPath}");

            if (options.Resolution.Width != 480 || options.Resolution.Height != 800)
            {
                options.Resolution = (480, 800);
                Console.WriteLine("Warning: X4 Pro requires 480x800 resolution. Setting to default.");
            }

            Console.WriteLine($"Converting EPUB to XTC: {epubPath} -> {outputPath}");
            Console.WriteLine($"Resolution: {options.Resolution.Width}x{options.Resolution.Height}");
            Console.WriteLine($"Vertical Writing: {options.EnableVerticalWriting}");
            Console.WriteLine($"Font: {options.FontFile ?? "Default"}");

            await CreateXtcFileAsync(epubPath, outputPath, options);
        }

        /// <summary>
        /// Parses the EPUB and writes the real XTC content.
        /// </summary>
        private void CreateXtcFile(string epubPath, string outputPath, XtcOptions options)
        {
            Console.WriteLine($"Parsing EPUB: {epubPath}");
            var (metadata, sections) = EPUBReader.Read(epubPath);
            Console.WriteLine($"  Title: {metadata.Title}");
            Console.WriteLine($"  Author: {metadata.Creator}");
            Console.WriteLine($"  Sections: {sections.Count}  Total blocks: {sections.Sum(s => s.Blocks.Count)}");

            var pages = new List<Page>();
            var curText = new StringBuilder();
            var curImages = new List<XtcImage>();
            int curChars = 0;

            void Flush()
            {
                if (curChars == 0 && curImages.Count == 0)
                    return;
                pages.Add(new Page { Text = curText.ToString(), Images = curImages });
                curText.Clear();
                curImages = new List<XtcImage>();
                curChars = 0;
            }

            foreach (var section in sections)
            {
                foreach (var block in section.Blocks)
                {
                    var text = block.Text;
                    int i = 0;
                    while (i < text.Length)
                    {
                        int remaining = MaxCharsPerPage - curChars;
                        int take = Math.Max(1, Math.Min(remaining, text.Length - i));
                        var chunk = text.Substring(i, take);
                        curText.Append(chunk);
                        curChars += take;
                        i += take;
                        if (curChars >= MaxCharsPerPage)
                            Flush();
                    }
                }

                foreach (var img in section.Images)
                {
                    if (curChars >= MaxCharsPerPage)
                        Flush();
                    curImages.Add(img);
                }
            }
            Flush();

            Console.WriteLine($"Generated {pages.Count} page(s) from {sections.Count} sections");
            WriteXtc(outputPath, options, metadata, pages);
            Console.WriteLine($"XTC file created at: {outputPath} ({pages.Count} pages)");
        }

        /// <summary>Async wrapper over CreateXtcFile.</summary>
        private async Task CreateXtcFileAsync(string epubPath, string outputPath, XtcOptions options)
        {
            await Task.Run(() => CreateXtcFile(epubPath, outputPath, options));
        }

        /// <summary>Writes the XTC file from parsed content.</summary>
        private void WriteXtc(string outputPath, XtcOptions options, EPUBMetadata metadata, List<Page> pages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<XTC>");
            sb.AppendLine("  <Metadata>");
            sb.AppendLine($"    <Width>{options.Resolution.Width}</Width>");
            sb.AppendLine($"    <Height>{options.Resolution.Height}</Height>");
            sb.AppendLine($"    <FontSize>{options.FontSize}</FontSize>");
            sb.AppendLine($"    <RubyFontSize>{options.RubyFontSize}</RubyFontSize>");
            sb.AppendLine($"    <LineSpacing>{options.LineSpacing}</LineSpacing>");
            sb.AppendLine($"    <MarginTop>{options.MarginTop}</MarginTop>");
            sb.AppendLine($"    <MarginBottom>{options.MarginBottom}</MarginBottom>");
            sb.AppendLine($"    <MarginRight>{options.MarginRight}</MarginRight>");
            sb.AppendLine($"    <VerticalWriting>{options.EnableVerticalWriting.ToString().ToLower()}</VerticalWriting>");
            if (!string.IsNullOrEmpty(metadata.Title))
                sb.AppendLine($"    <Title>{Escape(metadata.Title)}</Title>");
            if (!string.IsNullOrEmpty(metadata.Creator))
                sb.AppendLine($"    <Author>{Escape(metadata.Creator)}</Author>");
            if (!string.IsNullOrEmpty(metadata.Language))
                sb.AppendLine($"    <Language>{Escape(metadata.Language)}</Language>");
            sb.AppendLine("  </Metadata>");
            sb.AppendLine("  <Content>");
            sb.AppendLine("    <Pages>");
            foreach (var p in pages)
            {
                sb.AppendLine("      <Page>");
                sb.AppendLine("        <Text>");
                sb.AppendLine(Escape(p.Text));
                sb.AppendLine("        </Text>");
                if (p.Images.Count > 0)
                {
                    sb.AppendLine("          <Images>");
                    foreach (var img in p.Images)
                    {
                        var b64 = Convert.ToBase64String(img.Data);
                        sb.AppendLine($"          <Image data=\"{b64}\" name=\"{Escape(img.Name)}\"/>");
                    }
                    sb.AppendLine("          </Images>");
                }
                sb.AppendLine("      </Page>");
            }
            sb.AppendLine("    </Pages>");
            sb.AppendLine("  </Content>");
            sb.AppendLine("</XTC>");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private class Page
        {
            public string Text { get; set; }
            public List<XtcImage> Images { get; set; }
        }
    }
}
