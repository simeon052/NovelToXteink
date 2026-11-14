using System;
using System.Collections.Generic;
using Xunit;
using NovelToEink.Core;
using NovelToEink.Xtc;

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
        // レビュー#16-M1: 既定値と異なる値を設定して往復を実際に検証する。
        // OutputFolder は SettingsMap 外（エクスポート/適用されない）ため検証対象外。
        var settings = new AppSettings
        {
            Vertical = false,
            GrayscaleImages = false,
            EnableProofreading = false,
            RequestDelayMs = 3000,
            IsDarkMode = true,
            GenerateXtc = true,
            XtcDevice = XteinkDevice.X3,
            XtcFontFile = "NotoSansJP.ttf",
            XtcFontSize = 28,
            XtcTextThreshold = 180,
            XtcPaddingTop = 10,
            XtcPaddingBottom = 20,
            XtcPaddingLeft = 30,
            XtcPaddingRight = 40,
        };

        var json = LibraryExportImport.SerializeExport("1.0.0", [], settings);
        var (_, imported) = LibraryExportImport.DeserializeExport(json);

        // 既定値から適用：非既定値がそのまま復元されること。
        var target = new AppSettings();
        LibraryExportImport.ApplyImportedSettings(target, imported);

        Assert.Equal(settings.Vertical, target.Vertical);
        Assert.Equal(settings.GrayscaleImages, target.GrayscaleImages);
        Assert.Equal(settings.EnableProofreading, target.EnableProofreading);
        Assert.Equal(settings.RequestDelayMs, target.RequestDelayMs);
        Assert.Equal(settings.IsDarkMode, target.IsDarkMode);
        Assert.Equal(settings.GenerateXtc, target.GenerateXtc);
        Assert.Equal(settings.XtcDevice, target.XtcDevice);
        Assert.Equal(settings.XtcFontFile, target.XtcFontFile);
        Assert.Equal(settings.XtcFontSize, target.XtcFontSize);
        Assert.Equal(settings.XtcTextThreshold, target.XtcTextThreshold);
        Assert.Equal(settings.XtcPaddingTop, target.XtcPaddingTop);
        Assert.Equal(settings.XtcPaddingBottom, target.XtcPaddingBottom);
        Assert.Equal(settings.XtcPaddingLeft, target.XtcPaddingLeft);
        Assert.Equal(settings.XtcPaddingRight, target.XtcPaddingRight);
    }
}
