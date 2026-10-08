using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// LibraryStore を通したデータ消失の回帰テスト。
/// 壊れた JSON ファイルを読み込んで保存しても、元のデータが失われないことを確認する。
/// </summary>
public class DataLossRegressionTests
{
    [Fact]
    public void LibraryStore_CorruptLibraryJson_IsPreserved_EvenAfterSavingAnEmptyList()
    {
        using var dir = new TempDir();
        var store = new LibraryStore(dir.Path);
        const string broken = "[ {\"WorkId\": \"n1\", BROKEN";
        File.WriteAllText(store.FilePath, broken);

        var loaded = SilentLoad(() => store.Load());
        store.Save(loaded);   // 以前はこれが壊れたファイルを空の一覧で上書きして、元のデータを消していた

        Assert.Empty(loaded);
        Assert.Equal(broken, File.ReadAllText(store.FilePath + ".bad"));
        Assert.Equal("[]", File.ReadAllText(store.FilePath).Trim());
    }

    [Fact]
    public void LibraryStore_ValidJson_IsLoadedAndPreserved()
    {
        using var dir = new TempDir();
        var store = new LibraryStore(dir.Path);
        const string valid = "[{\"WorkId\":\"n1\",\"Title\":\"Test\"}]";
        File.WriteAllText(store.FilePath, valid);

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("n1", loaded[0].WorkId);
    }

    [Fact]
    public void LibraryStore_NonExistentFile_ReturnsEmptyList()
    {
        using var dir = new TempDir();
        var store = new LibraryStore(dir.Path);

        var loaded = store.Load();
        Assert.Empty(loaded);
    }

    private static List<LibraryEntry> SilentLoad(Func<List<LibraryEntry>> fn)
    {
        return fn();
    }
}
