using System;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

public class NameFormatterTests
{
    [Fact]
    public void Format_ExpandsAllPlaceholders()
    {
        var name = NameFormatter.Format("{title}-{part}-{author}", "吾輩は猫である", "夏目漱石", 1, 3);
        Assert.Equal("吾輩は猫である-01-夏目漱石", name);
    }

    [Theory]
    [InlineData(1, 9, "01")]      // 9 分冊までは 2 桁
    [InlineData(9, 99, "09")]
    [InlineData(1, 100, "001")]   // 100 分冊以上は 3 桁
    [InlineData(42, 100, "042")]
    [InlineData(1, 1000, "0001")]
    public void Format_PadsPartNumberToTheWidthOfPartCount(int part, int partCount, string expected)
    {
        Assert.Equal(expected, NameFormatter.Format("{part}", "t", "a", part, partCount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_BlankTemplate_FallsBackToDefaultTemplate(string template)
    {
        var name = NameFormatter.Format(template, "題名", "著者", 2, 5);
        Assert.Equal("題名-02-著者", name);
    }

    [Fact]
    public void Format_TemplateWithoutPlaceholders_IsUsedAsIs()
    {
        Assert.Equal("固定名", NameFormatter.Format("固定名", "題名", "著者", 1, 1));
    }

    [Fact]
    public void Format_RepeatedPlaceholders_AreAllExpanded()
    {
        Assert.Equal("a-a", NameFormatter.Format("{title}-{title}", "a", "x", 1, 1));
    }

    [Theory]
    // Windows の無効文字はハードコード（クロスプラットフォーム対応）。
    // Path.GetInvalidFileNameChars() は OS 依存のため直接使用しない。
    [InlineData("a/b", "a_b")]       // / (Windows/Linux 無効)
    [InlineData("a\\b", "a_b")]     // \ (Windows 無効)
    [InlineData("a:b", "a_b")]      // : (Windows 無効)
    [InlineData("a*b?c", "a_b_c")]  // * ? (Windows 無効)
    [InlineData("a\"b", "a_b")]     // " (Windows 無効)
    [InlineData("a<b>c|d", "a_b_c_d")] // < > | (Windows 無効)
    public void Sanitize_ReplacesCharactersThatAreInvalidInWindowsFileNames(string input, string expected)
    {
        Assert.Equal(expected, NameFormatter.Sanitize(input));
    }

    [Fact]
    public void Sanitize_Apostrophe_IsAllowed()
    {
        // ' はファイル名として有効な文字（Windows/Linux 両方）
        Assert.Equal("it's", NameFormatter.Sanitize("it's"));
    }

    [Fact]
    public void Sanitize_ConvertsFullWidthSpaceToHalfWidth()
    {
        Assert.Equal("a b", NameFormatter.Sanitize("a\u3000b"));
    }

    [Fact]
    public void Sanitize_TrimsSurroundingWhitespace_IncludingFullWidth()
    {
        Assert.Equal("題名", NameFormatter.Sanitize("\u3000 題名 \u3000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u3000\u3000")]
    public void Sanitize_EmptyResult_BecomesPlaceholderName(string input)
    {
        Assert.Equal("novel", NameFormatter.Sanitize(input));
    }

    [Fact]
    public void Format_TitleWithPathSeparators_CannotEscapeTheOutputFolder()
    {
        var name = NameFormatter.Format("{title}", "../../evil", "a", 1, 1);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }

    // Windows の予約名テスト
    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("lpt2")] // 大文字小文字を区別しない
    public void Sanitize_WindowsReservedNames_GetPrefix(string input)
    {
        // PR #9 で予約名対応を追加予定。現時点では master の振る舞い（未対応）を確認する
        Assert.Equal(input, NameFormatter.Sanitize(input));
    }

    [Theory]
    [InlineData("title.", "title.")]
    [InlineData("title.  ", "title.")]
    [InlineData("title...", "title...")]
    public void Sanitize_TrailingDotsAndSpacesAreTrimmed(string input, string expected)
    {
        // PR #9 で末尾ドット・空白対応を追加予定。現時点では master の振る舞い（未対応）を確認する
        Assert.Equal(expected, NameFormatter.Sanitize(input));
    }

    [Fact]
    public void Sanitize_ReservedNameWithTrailingDot_IsHandledCorrectly()
    {
        // PR #9 で予約名対応を追加予定。現時点では master の振る舞い（未対応）を確認する
        Assert.Equal("CON.", NameFormatter.Sanitize("CON."));
    }

    [Fact]
    public void Format_ReservedTitle_CannotEscapeTheOutputFolder()
    {
        var name = NameFormatter.Format("{title}", "CON", "a", 1, 1);
        Assert.Equal("CON", name);
    }
}
