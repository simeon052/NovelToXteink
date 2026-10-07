using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// 実際の保存先クラス（LibraryStore / ProofreadingService）を通した、データ消失の回帰テスト。
/// 壊れたファイルを読み込んだあとに保存しても、元の内容が失われない。
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
    public void LibraryStore_RoundTrip_PreservesEntries()
    {
        using var dir = new TempDir();
        var store = new LibraryStore(dir.Path);
        var entry = new LibraryEntry { Site = NovelSite.Syosetu, WorkId = "n2267be", Title = "異世界のんびり農家", Author = "内藤騎之介" };

        store.Save([entry]);
        var loaded = store.Load();

        var back = Assert.Single(loaded);
        Assert.Equal("n2267be", back.WorkId);
        Assert.Equal("異世界のんびり農家", back.Title);
        Assert.Equal(NovelSite.Syosetu, back.Site);
        Assert.False(File.Exists(store.FilePath + ".bad"));
    }

    [Fact]
    public void ProofreadingService_CorruptRulesFile_IsQuarantined_NotOverwrittenWithDefaults()
    {
        using var dir = new TempDir();
        var path = dir.File("proofreading.json");
        const string mine = "[ {\"Name\":\"自作ルール\",\"Type\":\"text_replace\", BROKEN";
        File.WriteAllText(path, mine);

        var svc = SilentLoad(() => ProofreadingService.Load(path));

        Assert.Equal(mine, File.ReadAllText(path + ".bad"));   // 手で編集したルールは残る
        Assert.True(File.Exists(path));                         // 使えるよう、初期ルールで新しく作られる
        Assert.NotEmpty(svc.GetEnabledTextReplacements());
    }

    [Fact]
    public void ProofreadingService_MissingFile_CreatesDefaults_WithoutQuarantine()
    {
        using var dir = new TempDir();
        var path = dir.File("proofreading.json");

        var svc = ProofreadingService.Load(path);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".bad"));
        Assert.NotEmpty(svc.GetEnabledTextReplacements());
    }

    [Fact]
    public void ProofreadingService_ValidRulesFile_KeepsUserRules_AndAddsNewDefaults()
    {
        using var dir = new TempDir();
        var path = dir.File("proofreading.json");
        ProofreadingService.SaveRules([new ProofreadingRule { Name = "自作ルール", Type = "text_replace", Pattern = "A", Replacement = "B", Enabled = true }], path);

        ProofreadingService.Load(path);   // 既定ルールの新規分は末尾に追記される
        var text = File.ReadAllText(path);

        Assert.Contains("自作ルール", text);                 // ユーザーのルールは消えない
        Assert.False(File.Exists(path + ".bad"));
    }

    // 退避時の ERROR ログは、このテストの関心事ではないので捨てる
    private static T SilentLoad<T>(Func<T> load)
    {
        var old = CoreLog.Sink;
        CoreLog.Sink = (_, _, _) => { };
        try { return load(); }
        finally { CoreLog.Sink = old; }
    }
}
