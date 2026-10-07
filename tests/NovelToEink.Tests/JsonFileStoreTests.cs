using System.Text.Json;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// 壊れた JSON を黙って既定値で上書きして、ユーザーのデータを失わないことの検証。
/// （以前は読み込み失敗を握りつぶして空の値を返し、次の Save() で壊れたファイルごと上書きしていた）
/// </summary>
public class JsonFileStoreTests
{
    private const string Broken = "[ {\"WorkId\": \"n1\", BROKEN";

    private static readonly JsonSerializerOptions Opts = new();

    [Fact]
    public void Load_MissingFile_ReturnsFallback_WithoutCreatingAnything()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");

        var result = JsonFileStore.Load(path, Opts, () => new List<int> { 7 });

        Assert.Equal([7], result);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Load_ValidFile_ReturnsContent_AndLeavesItAlone()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, "[1,2,3]");

        var result = JsonFileStore.Load(path, Opts, () => new List<int>());

        Assert.Equal([1, 2, 3], result);
        Assert.Equal("[1,2,3]", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Load_CorruptFile_MovesItToBad_AndReturnsFallback()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, Broken);

        var result = JsonFileStore.Load(path, Opts, () => new List<int> { 9 });

        Assert.Equal([9], result);
        Assert.Equal(Broken, File.ReadAllText(path + ".bad"));   // 壊れた内容はそのまま残る
        Assert.False(File.Exists(path));                          // 元の場所は空く（次の Save が新規作成する）
    }

    [Fact]
    public void Load_CorruptTwice_DoesNotOverwriteTheFirstBackup()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, Broken);
        JsonFileStore.Load(path, Opts, () => new List<int>());

        File.WriteAllText(path, "{ second corruption");
        JsonFileStore.Load(path, Opts, () => new List<int>());

        Assert.Equal(Broken, File.ReadAllText(path + ".bad"));
        Assert.Equal("{ second corruption", File.ReadAllText(path + ".bad-1"));
    }

    [Fact]
    public void Load_CorruptFile_ReportsAnError()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, Broken);
        var seen = new List<(string Level, string Message)>();
        var old = CoreLog.Sink;
        CoreLog.Sink = (lv, msg, _) => seen.Add((lv, msg));
        try
        {
            JsonFileStore.Load(path, Opts, () => new List<int>());
        }
        finally { CoreLog.Sink = old; }

        var entry = Assert.Single(seen);
        Assert.Equal("ERROR", entry.Level);
        Assert.Contains("a.json", entry.Message);
    }

    [Fact]
    public void Load_FileLockedByAnotherProcess_DoesNotQuarantine()
    {
        // 一時的な読み取り失敗（他のプロセスが開いている等）。ファイルは壊れていないので退避してはいけない。
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, "[1]");
        var old = CoreLog.Sink;
        CoreLog.Sink = (_, _, _) => { };
        try
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = JsonFileStore.Load(path, Opts, () => new List<int> { 5 });

            Assert.Equal([5], result);
        }
        finally { CoreLog.Sink = old; }

        Assert.False(File.Exists(path + ".bad"));
        Assert.Equal("[1]", File.ReadAllText(path));
    }

    [Fact]
    public void Load_JsonNull_ReturnsFallback_WithoutQuarantine()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, "null");

        var result = JsonFileStore.Load(path, Opts, () => new List<int> { 3 });

        Assert.Equal([3], result);
        Assert.False(File.Exists(path + ".bad"));   // 構文は正しいので壊れてはいない
    }
}
