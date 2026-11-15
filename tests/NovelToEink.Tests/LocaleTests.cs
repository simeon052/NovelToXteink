using System;
using System.Globalization;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// ロケール解決テスト。Override で ja/en を固定し、OS言語に依存しないことを保証する。
/// </summary>
public class LocaleTests
{
    [Fact]
    public void T_ResolvesJapaneseWhenOverrideJa()
    {
        Locale.Override = "ja";
        Assert.Equal("表紙なし", Locale.T("NoCover"));
    }

    [Fact]
    public void T_ResolvesEnglishWhenOverrideEn()
    {
        Locale.Override = "en";
        Assert.Equal("No cover", Locale.T("NoCover"));
    }

    [Fact]
    public void T_FormatsArguments()
    {
        Locale.Override = "en";
        Assert.Equal("EPUB file not found: a.epub", Locale.T("EpubNotFoundFmt", "a.epub"));
        Locale.Override = "ja";
        Assert.Equal("EPUBファイルが見つかりません: a.epub", Locale.T("EpubNotFoundFmt", "a.epub"));
    }

    [Fact]
    public void T_UnknownKeyReturnsKeyItself()
    {
        Locale.Override = "en";
        Assert.Equal("no_such_key", Locale.T("no_such_key"));
    }

    [Fact]
    public void Auto_ResolvesToOsUICulture()
    {
        Locale.Override = "auto";
        bool expectedEnglish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? false : true;
        Assert.Equal(expectedEnglish, Locale.IsEnglish());
    }

    [Fact]
    public void SetOverride_FiresChangedOnlyOnActualChange()
    {
        Locale.Override = "auto";
        int fired = 0;
        Locale.Changed += () => fired++;
        Locale.SetOverride("en");
        Assert.Equal(1, fired);
        Locale.SetOverride("en");
        Assert.Equal(1, fired);
        Locale.Changed -= () => fired++;
        Locale.Override = "auto";
    }

    [Fact]
    public void LanguageSetting_RoundTripsViaExportImport()
    {
        var settings = new AppSettings { Language = "en" };
        var json = LibraryExportImport.SerializeExport("1.0.0", [], settings);
        var (_, imported) = LibraryExportImport.DeserializeExport(json);
        var target = new AppSettings();
        LibraryExportImport.ApplyImportedSettings(target, imported);
        Assert.Equal("en", target.Language);
    }
}
