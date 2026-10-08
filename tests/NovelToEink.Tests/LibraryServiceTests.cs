using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// LibraryService のファイル名変更・削除をテストする。
/// 2.1 の RenameFiles 修正（一時ファイル退避）の回帰テストを含む。
/// </summary>
public class LibraryServiceTests
{
    [Fact]
    public void RenameFiles_ChangesFileNames()
    {
        using var dir = new TempDir();

        // Create dummy EPUB files
        File.WriteAllText(Path.Combine(dir.Path, "old-01-author.epub"), "dummy");
        File.WriteAllText(Path.Combine(dir.Path, "old-02-author.epub"), "dummy");

        var settings = new AppSettings { OutputFolder = dir.Path };
        var service = new LibraryService(settings);

        var entry = new LibraryEntry
        {
            NameTemplate = "{title}-{part}-{author}",
            Title = "new",
            Author = "writer",
            EpubParts = new List<string>
            {
                Path.Combine(dir.Path, "old-01-author.epub"),
                Path.Combine(dir.Path, "old-02-author.epub")
            }
        };

        service.RenameFiles(entry, "{title}-{part}-{author}");

        // Files should be renamed
        Assert.True(File.Exists(Path.Combine(dir.Path, "new-01-writer.epub")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "new-02-writer.epub")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "old-01-author.epub")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "old-02-author.epub")));
    }

    [Fact]
    public void RenameFiles_PreservesOriginalOnError()
    {
        using var dir = new TempDir();

        // Create source and target files
        File.WriteAllText(Path.Combine(dir.Path, "src.epub"), "source");
        File.WriteAllText(Path.Combine(dir.Path, "dst.epub"), "target");

        var settings = new AppSettings { OutputFolder = dir.Path };
        var service = new LibraryService(settings);

        var entry = new LibraryEntry
        {
            NameTemplate = "{title}-{part}-{author}",
            Title = "test",
            Author = "author",
            EpubParts = new List<string>
            {
                Path.Combine(dir.Path, "src.epub")
            }
        };

        // Try to rename src.epub -> dst.epub (target exists)
        // This should succeed: move target to .renaming.tmp, then move src to dst, then delete .renaming.tmp
        service.RenameFiles(entry, "{title}-{part}-{author}");

        // The rename should have succeeded - src moved to dst
        Assert.True(File.Exists(Path.Combine(dir.Path, "test-01-author.epub")));
    }

    [Fact]
    public void RenameFiles_NoChangeWhenNoConflict()
    {
        using var dir = new TempDir();

        File.WriteAllText(Path.Combine(dir.Path, "a.epub"), "dummy");

        var settings = new AppSettings { OutputFolder = dir.Path };
        var service = new LibraryService(settings);

        var entry = new LibraryEntry
        {
            NameTemplate = "{title}-{part}-{author}",
            Title = "b",
            Author = "c",
            EpubParts = new List<string>
            {
                Path.Combine(dir.Path, "a.epub")
            }
        };

        service.RenameFiles(entry, "{title}-{part}-{author}");

        Assert.True(File.Exists(Path.Combine(dir.Path, "b-01-c.epub")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "a.epub")));
    }

    [Fact]
    public void Remove_WithDeleteFiles_RemovesAllFiles()
    {
        using var dir = new TempDir();

        // Create dummy files
        File.WriteAllText(Path.Combine(dir.Path, "part1.epub"), "dummy");
        File.WriteAllText(Path.Combine(dir.Path, "part2.epub"), "dummy");
        File.WriteAllText(Path.Combine(dir.Path, "cover.jpg"), "dummy");

        var settings = new AppSettings { OutputFolder = dir.Path };
        var service = new LibraryService(settings);

        // Add a library entry
        var store = new LibraryStore(dir.Path);
        store.Save(new List<LibraryEntry>
        {
            new LibraryEntry
            {
                EpubParts = new List<string>
                {
                    Path.Combine(dir.Path, "part1.epub"),
                    Path.Combine(dir.Path, "part2.epub")
                },
                CoverImagePath = Path.Combine(dir.Path, "cover.jpg")
            }
        });

        var entries = store.Load();
        Assert.Single(entries);

        service.Remove(entries[0], deleteFiles: true);

        // All files should be deleted
        Assert.False(File.Exists(Path.Combine(dir.Path, "part1.epub")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "part2.epub")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "cover.jpg")));
    }

    [Fact]
    public void Remove_WithoutDeleteFiles_KeepsFiles()
    {
        using var dir = new TempDir();

        File.WriteAllText(Path.Combine(dir.Path, "part1.epub"), "dummy");

        var settings = new AppSettings { OutputFolder = dir.Path };
        var service = new LibraryService(settings);

        var store = new LibraryStore(dir.Path);
        store.Save(new List<LibraryEntry>
        {
            new LibraryEntry
            {
                EpubParts = new List<string> { Path.Combine(dir.Path, "part1.epub") }
            }
        });

        var entries = store.Load();
        service.Remove(entries[0], deleteFiles: false);

        // File should still exist
        Assert.True(File.Exists(Path.Combine(dir.Path, "part1.epub")));
    }
}
