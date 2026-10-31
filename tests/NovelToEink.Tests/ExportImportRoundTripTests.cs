using System;
using System.Collections.Generic;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// Copilot review #13-Minor: エクスポート→インポート往復（Round-trip）テスト。
/// SerializeExport → DeserializeExport → ApplyImportedSettings で
/// ライブラリ項目と設定が元の値と一致すること。
/// </summary>
public class ExportImportRoundTripTests
{
    [Fact]
    public void RoundTrip_PreservesLibraryEntryFields()
    {
        var entry = new LibraryEntry
        {
            Url = "https://kakuyomu.jp/works/w1",
            WorkId = "w1",
            Title = "テスト物語",
            Author = "著者名",
            Description = "説明",
            NameTemplate = "{title}-{part}-{author}",
            Status = UpdateStatus.UpToDate,
            EpisodeCount = 5,
            Site = NovelSite.Kakuyomu,
            LastEpisodeTitle = "最終話",
            EpubPath = "テスト物語-1.epub",
            IsCompleted = true,
            CoverPending = false
        };
        var settings = new AppSettings();

        var json = LibraryExportImport.SerializeExport("1.0.0", [entry], settings);
        var (library, imported) = LibraryExportImport.DeserializeExport(json);

        Assert.Single(library);
        var r = library[0];
        Assert.Equal(entry.Url, r.Url);
        Assert.Equal(entry.WorkId, r.WorkId);
        Assert.Equal(entry.Title, r.Title);
        Assert.Equal(entry.Author, r.Author);
        Assert.Equal(entry.Description, r.Description);
        Assert.Equal(entry.NameTemplate, r.NameTemplate);
        Assert.Equal(entry.Status, r.Status);
        Assert.Equal(entry.EpisodeCount, r.EpisodeCount);
        Assert.Equal(entry.Site, r.Site);
        Assert.Equal(entry.LastEpisodeTitle, r.LastEpisodeTitle);
        Assert.Equal(entry.EpubPath, r.EpubPath);
        Assert.Equal(entry.IsCompleted, r.IsCompleted);
    }

    [Fact]
    public void RoundTrip_PreservesSettingsViaApply()
    {
        var settings = new AppSettings();

        var json = LibraryExportImport.SerializeExport("1.0.0", [], settings);
        var (_, imported) = LibraryExportImport.DeserializeExport(json);

        var target = new AppSettings();
        LibraryExportImport.ApplyImportedSettings(target, imported);

        Assert.Equal(settings.OutputFolder, target.OutputFolder);
        Assert.Equal(settings.Vertical, target.Vertical);
        Assert.Equal(settings.GrayscaleImages, target.GrayscaleImages);
        Assert.Equal(settings.XtcDevice, target.XtcDevice);
    }
}
