using NovelToEink.Core;
using Xunit;

namespace NovelToEink.Tests;

public class TitleCleanerTests
{
    [Fact]
    public void Clean_RemovesBookOnSaleTag()
    {
        Assert.Equal("とんでもなく強力な冒険者",
            TitleCleaner.Clean("【書籍発売中】とんでもなく強力な冒険者"));
    }

    [Fact]
    public void Clean_RemovesSimilarMarketingTags()
    {
        Assert.Equal("転生したら〜", TitleCleaner.Clean("【コミカライズ】転生したら〜"));
        Assert.Equal("物語", TitleCleaner.Clean("【アニメ化】物語"));
        Assert.Equal("物語", TitleCleaner.Clean("[書籍発売中]物語"));
        Assert.Equal("物語", TitleCleaner.Clean("〔電子書籍〕物語"));
    }

    [Fact]
    public void Clean_KeepsNonMarketingBrackets()
    {
        Assert.Equal("【重要】な話", TitleCleaner.Clean("【重要】な話"));
        Assert.Equal("そのままのタイトル", TitleCleaner.Clean("そのままのタイトル"));
    }

    [Fact]
    public void Clean_RemovesTagMidTitleAndTrimsSpaces()
    {
        Assert.Equal("すばらしき世界", TitleCleaner.Clean("すばらしき世界 【書籍発売中】"));
        Assert.Equal("辺境の【杖職人】が、魔法杖最高だと気付くまで", TitleCleaner.Clean("辺境の【杖職人】が、魔法杖最高だと気付くまで"));
    }

    [Fact]
    public void Clean_RemovesSaleTagWithVolumeNumber()
    {
        Assert.Equal("凡人探索者のたのしい現代ダンジョンライフ", TitleCleaner.Clean("凡人探索者のたのしい現代ダンジョンライフ 【書籍6巻発売中！】"));
        Assert.Equal("新刊発売中！の物語", TitleCleaner.Clean("新刊発売中！の物語")); // 括弧なしはそのまま
    }

    [Fact]
    public void Clean_EmptyAndNullSafe()
    {
        Assert.Equal("", TitleCleaner.Clean(""));
        Assert.Null(TitleCleaner.Clean(null));
    }
}
