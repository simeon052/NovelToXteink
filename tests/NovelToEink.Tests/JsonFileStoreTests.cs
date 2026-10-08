using System;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// 実際の保存先クラス（JsonFileStore）を通した、データ消失の回帰テスト。
/// 壊れたファイルを読み込んだあとに保存しても、元の内容が失われない。
/// </summary>
public class JsonFileStoreTests
{
    [Fact]
    public void Load_NonExistentFile_ReturnsFallback()
    {
        var result = JsonFileStore.Load<string>("/nonexistent/path.json", null, () => "default");
        Assert.Equal("default", result);
    }

    [Fact]
    public void Load_ValidJson_ReturnsDeserializedValue()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path + "/data.json", "\"hello\"");
        
        var result = JsonFileStore.Load<string>(dir.Path + "/data.json", null, () => "fallback");
        Assert.Equal("hello", result);
        Assert.False(File.Exists(dir.Path + "/data.json.bad"));
    }

    [Fact]
    public void Load_CorruptJson_IsQuarantined_AndReturnsFallback()
    {
        using var dir = new TempDir();
        const string broken = "[ {\"key\": BROKEN";
        File.WriteAllText(dir.Path + "/data.json", broken);
        
        var result = JsonFileStore.Load<string>(dir.Path + "/data.json", null, () => "fallback");
        
        Assert.Equal("fallback", result);
        Assert.True(File.Exists(dir.Path + "/data.json.bad"));
        Assert.Equal(broken, File.ReadAllText(dir.Path + "/data.json.bad").Trim());
    }

    [Fact]
    public void Load_CorruptJson_OriginalContentIsPreservedInBadFile()
    {
        using var dir = new TempDir();
        const string original = "{\"id\": 42, \"name\": \"important data\"}";
        // Simulate corruption by appending invalid JSON
        File.WriteAllText(dir.Path + "/data.json", original + " BROKEN{");
        
        var result = JsonFileStore.Load<string>(dir.Path + "/data.json", null, () => "{}");
        
        Assert.Equal("{}", result);
        // The quarantined file should contain the original corrupted content
        var badContent = File.ReadAllText(dir.Path + "/data.json.bad");
        Assert.Contains("42", badContent);
    }

    [Fact]
    public void QuarantineCorruptFile_RenamedWithIncrementalSuffix_WhenBadExists()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path + "/data.json", "corrupted");
        File.WriteAllText(dir.Path + "/data.json.bad", "previous bad");
        
        var result = JsonFileStore.QuarantineCorruptFile(dir.Path + "/data.json");
        
        Assert.Equal(dir.Path + "/data.json.bad-1", result);
        Assert.True(File.Exists(dir.Path + "/data.json.bad")); // previous .bad is untouched
    }

    [Fact]
    public void Load_JsonNullValue_ReturnsFallback()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path + "/data.json", "null");
        
        var result = JsonFileStore.Load<string>(dir.Path + "/data.json", null, () => "fallback");
        
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void Load_EmptyString_ReturnsFallback()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path + "/data.json", "");
        
        var result = JsonFileStore.Load<string>(dir.Path + "/data.json", null, () => "fallback");
        
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void Load_JsonArray_ReturnsDeserializedValue()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path + "/data.json", "[1, 2, 3]");
        
        var result = JsonFileStore.Load<List<int>>(dir.Path + "/data.json", null, () => new List<int>());
        
        Assert.Equal(new List<int> { 1, 2, 3 }, result);
    }
}
