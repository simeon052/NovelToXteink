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
    [InlineData(1, 100, "001")]   // 100 分冊以上は 3 桁。並べ替えで 10 が 2 の前に来ないようにする
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
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    [InlineData("a:b", "a_b")]
    [InlineData("a*b?c", "a_b_c")]
    [InlineData("a\"b", "a_b")]
    [InlineData("a<b>c|d", "a_b_c_d")]
    public void Sanitize_ReplacesCharactersThatAreInvalidInWindowsFileNames(string input, string expected)
    {
        Assert.Equal(expected, NameFormatter.Sanitize(input));
    }

    [Fact]
    public void Sanitize_ConvertsFullWidthSpaceToHalfWidth()
    {
        Assert.Equal("a b", NameFormatter.Sanitize("a　b"));
    }

    [Fact]
    public void Sanitize_TrimsSurroundingWhitespace_IncludingFullWidth()
    {
        Assert.Equal("題名", NameFormatter.Sanitize("　 題名 　"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("　　")]
    public void Sanitize_EmptyResult_BecomesPlaceholderName(string input)
    {
        Assert.Equal("novel", NameFormatter.Sanitize(input));
    }

    [Fact]
    public void Format_TitleWithPathSeparators_CannotEscapeTheOutputFolder()
    {
        // 作品名に / や .. が入っていても、出力フォルダの外へは書けない
        var name = NameFormatter.Format("{title}", "../../evil", "a", 1, 1);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }
}
