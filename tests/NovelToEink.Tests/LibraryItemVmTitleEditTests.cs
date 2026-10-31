using System.Linq;
using Xunit;
using NovelToEink.Core;
using NovelToEink.App;

namespace NovelToEink.Tests;

/// <summary>
/// LibraryItemVm のダブルクリックタイトル編集（Start/Commit/Cancel）をテストする。
/// 確定は Core の UpdateTitle 経由で library.json に永続化される。
/// </summary>
public class LibraryItemVmTitleEditTests
{
    private static (LibraryService service, LibraryEntry entry, TempDir dir) Setup()
    {
        var dir = new TempDir();
        var settings = new AppSettings { OutputFolder = dir.Path };
        var service = new LibraryService(settings);
        var entry = new LibraryEntry
        {
            Title = "[連載版]物語",
            WorkId = "w1",
            Site = NovelSite.Kakuyomu,
            Url = "https://kakuyomu.jp/work/w1"
        };
        service.Entries.Add(entry);
        return (service, entry, dir);
    }

    [Fact]
    public void StartEdit_ExposesTitleAndEditingFlag()
    {
        var (service, entry, dir) = Setup();
        using (dir)
        {
            var vm = new LibraryItemVm(entry, service.UpdateTitle);
            vm.StartTitleEdit();

            Assert.True(vm.EditingTitle);
            Assert.Equal("[連載版]物語", vm.TitleEdit);
        }
    }

    [Fact]
    public void CommitEdit_PersistsViaCallback()
    {
        var (service, entry, dir) = Setup();
        using (dir)
        {
            var vm = new LibraryItemVm(entry, service.UpdateTitle);
            vm.StartTitleEdit();
            vm.TitleEdit = "新しい物語";
            vm.CommitTitleEdit();

            Assert.False(vm.EditingTitle);
            Assert.Equal("新しい物語", entry.Title);
            var reloaded = new LibraryStore(dir.Path).Load();
            Assert.Equal("新しい物語", reloaded.Single(e => e.WorkId == "w1").Title);
        }
    }

    [Fact]
    public void EmptyCommit_CancelsWithoutPersist()
    {
        var (service, entry, dir) = Setup();
        using (dir)
        {
            var persistCalls = 0;
            var vm = new LibraryItemVm(entry, (e, t) => { persistCalls++; service.UpdateTitle(e, t); });
            vm.StartTitleEdit();
            vm.TitleEdit = "   ";
            vm.CommitTitleEdit();

            Assert.False(vm.EditingTitle);
            Assert.Equal(0, persistCalls);
            Assert.Equal("[連載版]物語", entry.Title);
        }
    }

    [Fact]
    public void CancelEdit_KeepsOriginal()
    {
        var (service, entry, dir) = Setup();
        using (dir)
        {
            var vm = new LibraryItemVm(entry, service.UpdateTitle);
            vm.StartTitleEdit();
            vm.TitleEdit = "破棄される文字列";
            vm.CancelTitleEdit();

            Assert.False(vm.EditingTitle);
            Assert.Equal("[連載版]物語", entry.Title);
        }
    }
}
