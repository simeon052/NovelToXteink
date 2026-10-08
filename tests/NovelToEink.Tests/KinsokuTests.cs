using Xunit;
using NovelToEink.Xtc;

namespace NovelToEink.Tests;

public class KinsokuTests
{
    [Theory]
    [InlineData('、')]
    [InlineData('。')]
    [InlineData('」')]
    [InlineData('』')]
    [InlineData('）')]
    [InlineData('ー')]   // 長音
    [InlineData('っ')]   // 小書き仮名
    [InlineData('ゃ')]
    [InlineData('ッ')]
    [InlineData('ョ')]
    [InlineData('！')]
    [InlineData('？')]
    [InlineData('々')]
    public void ClosingPunctuationAndSmallKana_AreForbiddenAtLineStart(char c)
    {
        Assert.True(Kinsoku.IsForbiddenAtLineStart(c));
    }

    [Theory]
    [InlineData('「')]
    [InlineData('『')]
    [InlineData('（')]
    [InlineData('【')]
    [InlineData('〈')]
    [InlineData('《')]
    public void OpeningBrackets_AreForbiddenAtLineEnd(char c)
    {
        Assert.True(Kinsoku.IsForbiddenAtLineEnd(c));
    }

    [Theory]
    [InlineData('あ')]
    [InlineData('漢')]
    [InlineData('A')]
    [InlineData('1')]
    [InlineData('カ')]
    public void OrdinaryCharacters_AreForbiddenNowhere(char c)
    {
        Assert.False(Kinsoku.IsForbiddenAtLineStart(c));
        Assert.False(Kinsoku.IsForbiddenAtLineEnd(c));
    }

    [Theory]
    [InlineData('「')]
    [InlineData('（')]
    public void OpeningBrackets_AreNotForbiddenAtLineStart(char c)
    {
        Assert.False(Kinsoku.IsForbiddenAtLineStart(c));
    }

    [Theory]
    [InlineData('」')]
    [InlineData('、')]
    [InlineData('。')]
    public void ClosingBracketsAndPunctuation_AreNotForbiddenAtLineEnd(char c)
    {
        Assert.False(Kinsoku.IsForbiddenAtLineEnd(c));
    }

    [Fact]
    public void NoCharacterIsForbiddenAtBothLineStartAndLineEnd()
    {
        for (int i = 0; i <= 0xFFFF; i++)
        {
            var c = (char)i;
            Assert.False(Kinsoku.IsForbiddenAtLineStart(c) && Kinsoku.IsForbiddenAtLineEnd(c),
                $"U+{i:X4} '{c}' が行頭・行末の両方で禁止になっている");
        }
    }
}
