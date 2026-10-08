using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// EpubBuilder が生成する EPUB の構造を検証するテスト。
/// 整形式 XML か、必須ファイル（mimetype, container.xml）があるかを確認する。
/// </summary>
public class EpubBuilderTests
{
    [Fact]
    public void Build_GeneratesValidEpubWithRequiredFiles()
    {
        using var dir = new TempDir();
        var outputPath = Path.Combine(dir.Path, "test.epub");

        // Create a minimal NovelDownload with 1 episode and no images
        var episodes = new List<EpisodeContent>
        {
            new EpisodeContent
            {
                Ref = new EpisodeRef { Index = 1, Title = "第一章", Url = "https://example.com/ch1" },
                BodyHtml = "<p>本文です。</p>",
                ImageUrls = Array.Empty<string>()
            }
        };

        var novel = new NovelDownload
        {
            Metadata = new NovelMetadata
            {
                Site = NovelSite.Syosetu,
                WorkId = "n12345",
                Title = "テスト作品",
                Author = "テスト著者",
                Url = "https://ncode.syosetu.com/n12345/",
                IsCompleted = false
            },
            Episodes = episodes,
            Images = Array.Empty<ScrapedImage>()
        };

        var options = new EpubOptions { WritingMode = WritingMode.Vertical };

        var result = EpubBuilder.Build(novel, null, options, outputPath);

        Assert.NotNull(result);
        Assert.True(File.Exists(outputPath));

        // Verify EPUB structure using ZipArchive
        using var archive = new ZipArchive(File.OpenRead(outputPath));
        var entries = archive.Entries.Select(e => e.FullName).ToHashSet();

        // Required files must exist
        Assert.Contains("mimetype", entries);
        Assert.Contains("META-INF/container.xml", entries);
        Assert.Contains("OEBPS/content.opf", entries);
        Assert.Contains("OEBPS/nav.xhtml", entries);
        Assert.Contains("OEBPS/style.css", entries);

        // mimetype must be first entry and uncompressed
        var mimetypeEntry = archive.GetEntry("mimetype");
        Assert.NotNull(mimetypeEntry);
    }

    [Fact]
    public void Build_GeneratesIntactXhtmlFiles()
    {
        using var dir = new TempDir();
        var outputPath = Path.Combine(dir.Path, "test.epub");

        var episodes = new List<EpisodeContent>
        {
            new EpisodeContent
            {
                Ref = new EpisodeRef { Index = 1, Title = "第一章", Url = "https://example.com/ch1" },
                BodyHtml = "<p>本文です。</p>",
                ImageUrls = Array.Empty<string>()
            }
        };

        var novel = new NovelDownload
        {
            Metadata = new NovelMetadata
            {
                Site = NovelSite.Syosetu,
                WorkId = "n12345",
                Title = "テスト作品",
                Author = "テスト著者",
                Url = "https://ncode.syosetu.com/n12345/",
                IsCompleted = false
            },
            Episodes = episodes,
            Images = Array.Empty<ScrapedImage>()
        };

        var options = new EpubOptions { WritingMode = WritingMode.Vertical };

        var result = EpubBuilder.Build(novel, null, options, outputPath);

        using var archive = new ZipArchive(File.OpenRead(outputPath));

        // Check nav.xhtml is well-formed XML
        var navEntry = archive.GetEntry("OEBPS/nav.xhtml");
        Assert.NotNull(navEntry);
        using var navStream = navEntry.Open();
        var navDoc = XDocument.Load(navStream);
        Assert.NotNull(navDoc.Root);
        Assert.Equal("http://www.w3.org/1999/xhtml", navDoc.Root.Name.Namespace.ToString());

        // Check content.opf is well-formed XML
        var opfEntry = archive.GetEntry("OEBPS/content.opf");
        Assert.NotNull(opfEntry);
        using var opfStream = opfEntry.Open();
        var opfDoc = XDocument.Load(opfStream);
        Assert.NotNull(opfDoc.Root);
    }

    [Fact]
    public void Build_MultipleEpisodes_GeneratesAllXhtmlFiles()
    {
        using var dir = new TempDir();
        var outputPath = Path.Combine(dir.Path, "test.epub");

        var episodes = Enumerable.Range(1, 5).Select(i => new EpisodeContent
        {
            Ref = new EpisodeRef { Index = i, Title = $"第{i}章", Url = $"https://example.com/ch{i}" },
            BodyHtml = $"<p>本文{i}です。</p>",
            ImageUrls = Array.Empty<string>()
        }).ToList();

        var novel = new NovelDownload
        {
            Metadata = new NovelMetadata
            {
                Site = NovelSite.Syosetu,
                WorkId = "n12345",
                Title = "テスト作品",
                Author = "テスト著者",
                Url = "https://ncode.syosetu.com/n12345/",
                IsCompleted = false
            },
            Episodes = episodes,
            Images = Array.Empty<ScrapedImage>()
        };

        var options = new EpubOptions { WritingMode = WritingMode.Vertical };

        var result = EpubBuilder.Build(novel, null, options, outputPath);

        Assert.Equal(5, result.EpisodeCount);

        using var archive = new ZipArchive(File.OpenRead(outputPath));
        var textEntries = archive.Entries.Where(e => e.FullName.StartsWith("OEBPS/text/")).ToList();
        Assert.Equal(5, textEntries.Count);
    }

    [Fact]
    public void Build_Cover_GeneratesCoverPageAndImage()
    {
        using var dir = new TempDir();
        var outputPath = Path.Combine(dir.Path, "test.epub");

        // 1x1 PNG as cover image
        var coverData = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        var episodes = new List<EpisodeContent>
        {
            new EpisodeContent
            {
                Ref = new EpisodeRef { Index = 1, Title = "第一章", Url = "https://example.com/ch1" },
                BodyHtml = "<p>本文です。</p>",
                ImageUrls = Array.Empty<string>()
            }
        };

        var novel = new NovelDownload
        {
            Metadata = new NovelMetadata
            {
                Site = NovelSite.Syosetu,
                WorkId = "n12345",
                Title = "テスト作品",
                Author = "テスト著者",
                Url = "https://ncode.syosetu.com/n12345/",
                IsCompleted = false
            },
            Episodes = episodes,
            Images = Array.Empty<ScrapedImage>()
        };

        var cover = new ScrapedImage { Data = coverData, Width = 1, Height = 1, SourceUrl = "https://example.com/cover.jpg" };
        var options = new EpubOptions { WritingMode = WritingMode.Vertical };

        var result = EpubBuilder.Build(novel, cover, options, outputPath);

        Assert.True(result.ImageCount >= 1); // at least cover image

        using var archive = new ZipArchive(File.OpenRead(outputPath));
        var entries = archive.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("OEBPS/cover.xhtml", entries);
        Assert.Contains("OEBPS/images/cover.jpg", entries);
    }

    [Fact]
    public void Build_WithInlineImages_GeneratesImageFiles()
    {
        using var dir = new TempDir();
        var outputPath = Path.Combine(dir.Path, "test.epub");

        // 1x1 PNG as inline image
        var imageData = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        var episodes = new List<EpisodeContent>
        {
            new EpisodeContent
            {
                Ref = new EpisodeRef { Index = 1, Title = "第一章", Url = "https://example.com/ch1" },
                BodyHtml = "<p>本文<img src=\"https://example.com/img1.jpg\"/>です。</p>",
                ImageUrls = new List<string> { "https://example.com/img1.jpg" }
            }
        };

        var novel = new NovelDownload
        {
            Metadata = new NovelMetadata
            {
                Site = NovelSite.Syosetu,
                WorkId = "n12345",
                Title = "テスト作品",
                Author = "テスト著者",
                Url = "https://ncode.syosetu.com/n12345/",
                IsCompleted = false
            },
            Episodes = episodes,
            Images = new List<ScrapedImage>
            {
                new ScrapedImage { Data = imageData, Width = 1, Height = 1, SourceUrl = "https://example.com/img1.jpg" }
            }
        };

        var options = new EpubOptions { WritingMode = WritingMode.Vertical, IncludeInlineImages = true };

        var result = EpubBuilder.Build(novel, null, options, outputPath);

        Assert.True(result.ImageCount >= 1);

        using var archive = new ZipArchive(File.OpenRead(outputPath));
        var imageEntries = archive.Entries.Where(e => e.FullName.StartsWith("OEBPS/images/")).ToList();
        Assert.NotEmpty(imageEntries);
    }
}
