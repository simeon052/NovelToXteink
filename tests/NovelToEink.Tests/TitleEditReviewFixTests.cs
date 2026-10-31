using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NovelToEink.Core;
using NovelToEink.App;
using Xunit;

namespace NovelToEink.Tests;

/// <summary>
/// PR #15 レビュー（OpenCode）指摘の回帰テスト:
/// C-1: ユーザー編集タイトルフラグ、M-1: アトミック保存、M-2: 保存失敗、N-1: 異種括弧の誤マッチ。
/// </summary>
public class TitleEditReviewFixTests
{
    // N-1: 開き/閉じ括弧の対が不一致ならマッチしない
    [Fact]
    public void Clean_DoesNotMixBracketPairs()
    {
        Assert.Equal("[連載版】物語", TitleCleaner.Clean("[連載版】物語"));
        Assert.Equal("【書籍発売中〕物語", TitleCleaner.Clean("【書籍発売中〕物語"));
    }

    // C-1: UpdateTitle はフラグを立て、永続化され、ApplyTitle はフラグ立て済みタイトルを書き戻さない
    [Fact]
    public void UpdateTitle_SetsUserEditedFlagAndPersistsIt()
    {
        using var dir = new TempDir();
        var service = new LibraryService(new AppSettings { OutputFolder = dir.Path });
        var entry = new LibraryEntry
        {
            Title = "[連載版]物語",
            WorkId = "w1",
            Site = NovelSite.Kakuyomu,
            Url = "https://kakuyomu.jp/work/w1"
        };
        service.Entries.Add(entry);

        service.UpdateTitle(entry, "物語");
        Assert.True(entry.TitleUserEdited);

        var reloaded = new LibraryStore(dir.Path).Load().Single(e => e.WorkId == "w1");
        Assert.True(reloaded.TitleUserEdited); // library.json に保存されている
    }

    [Fact]
    public void ApplyTitle_RespectsUserEditedFlag()
    {
        var edited = new LibraryEntry
        {
            Title = "ユーザー確定した題名",
            WorkId = "w1",
            Site = NovelSite.Kakuyomu,
            Url = "https://kakuyomu.jp/work/w1",
            TitleUserEdited = true
        };
        LibraryService.ApplyTitle(edited, "[連載版]サイトの題名");
        Assert.Equal("ユーザー確定した題名", edited.Title); // 更新チェックで上書きされない

        var fresh = new LibraryEntry
        {
            Title = "旧",
            WorkId = "w2",
            Site = NovelSite.Kakuyomu,
            Url = "https://kakuyomu.jp/work/w2"
        };
        LibraryService.ApplyTitle(fresh, "[連載版]物語");
        Assert.Equal("物語", fresh.Title); // フラグなしは通常どおり Clean
    }

    // M-1: 保存は一時ファイル+スワップ（アトミック）。完了後 .tmp は残らない
    [Fact]
    public void Save_IsAtomic_LeavesNoTempFile()
    {
        using var dir = new TempDir();
        var store = new LibraryStore(dir.Path);
        store.Save(new[] { new LibraryEntry { Title = "T", WorkId = "w1", Site = NovelSite.Kakuyomu, Url = "u" } });

        Assert.False(File.Exists(Path.Combine(dir.Path, "library.json.tmp")));
        var reloaded = store.Load();
        Assert.Single(reloaded);
    }

    [Fact]
    public void ConcurrentPersistAndUpdateTitle_ProducesValidJson()
    {
        using var dir = new TempDir();
        var service = new LibraryService(new AppSettings { OutputFolder = dir.Path });
        var entry = new LibraryEntry { Title = "T", WorkId = "w1", Site = NovelSite.Kakuyomu, Url = "u" };
        service.Entries.Add(entry);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            if (i % 2 == 0) service.Persist();
            else service.UpdateTitle(entry, $"X{i}");
        })).ToArray();
        Task.WaitAll(tasks);

        // 破損していれば .bad に退避され Load() は空になる（JsonFileStore の救済）
        Assert.False(File.Exists(Path.Combine(dir.Path, "library.json.bad")));
        var reloaded = new LibraryStore(dir.Path).Load();
        Assert.Single(reloaded);
    }

    // M-2: 保存失敗時は編集状態を維持し、エラーを報告（アプリはクラッシュしない）
    [Fact]
    public void CommitEdit_PersistFailure_KeepsEditingAndReportsError()
    {
        using var dir = new TempDir();
        var service = new LibraryService(new AppSettings { OutputFolder = dir.Path });
        var entry = new LibraryEntry { Title = "原題名", WorkId = "w1", Site = NovelSite.Kakuyomu, Url = "u" };
        service.Entries.Add(entry);

        string? error = null;
        var vm = new LibraryItemVm(entry, (e, t) => throw new IOException("テスト用のディスクエラー"));
        vm.TitleEditError += msg => error = msg;
        vm.StartTitleEdit();
        vm.TitleEdit = "新題名";
        vm.CommitTitleEdit();

        Assert.True(vm.EditingTitle);        // 失敗時は編集状態を維持（再試行可能）
        Assert.NotNull(error);
        Assert.Equal("原題名", entry.Title); // エントリは無変化
    }
}
