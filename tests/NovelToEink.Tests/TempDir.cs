using System;
using System.IO;
using Xunit;

namespace NovelToEink.Tests;

/// <summary>自動削除付きの一時ディレクトリ。</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NovelToEinkTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* テスト環境のクリーンアップ失敗は許容 */ }
    }
}
