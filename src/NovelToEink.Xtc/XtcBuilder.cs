namespace NovelToEink.Xtc;

/// <summary>
/// ページ数を事前に知らなくても XTC を組み立てられるビルダー。
/// XTG ブロックを一時ファイルへ書き出し、<see cref="Complete"/> でページ数が確定してから
/// ヘッダーとインデックスを先頭に付けて本体へ結合する。
/// </summary>
/// <remarks>
/// XTC はヘッダー直後にインデックステーブルが来るため、データ開始位置の決定にページ数が要る。
/// 全ページをメモリに抱えると 1500 ページ級で 80MB を超えるので、一時ファイルへ退避する。
/// </remarks>
public sealed class XtcBuilder : IDisposable
{
    private readonly List<(int Size, int Width, int Height)> _pages = [];
    private readonly string _spoolPath;
    private readonly FileStream _spool;
    private bool _disposed;

    /// <summary>これまでに追加したページ数。</summary>
    public int PageCount => _pages.Count;

    /// <summary>ビルダーを作る。一時ファイルは <see cref="Dispose"/> で削除される。</summary>
    public XtcBuilder()
    {
        _spoolPath = Path.Combine(Path.GetTempPath(), $"noveltoeink-xtc-{Guid.NewGuid():N}.tmp");
        _spool = new FileStream(_spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1 << 16, FileOptions.DeleteOnClose);
    }

    /// <summary>XTG ブロックを 1 ページ追加する。</summary>
    public void AddPage(ReadOnlySpan<byte> xtgBlob, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _spool.Write(xtgBlob);
        _pages.Add((xtgBlob.Length, width, height));
    }

    /// <summary>収集したページを XTC ファイルとして書き出す。</summary>
    /// <param name="outputPath">出力先の .xtc パス。</param>
    /// <param name="readDirection">読み進む向き。</param>
    public void Complete(string outputPath, XtcReadDirection readDirection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pages.Count == 0)
            throw new InvalidOperationException("ページが 1 枚もありません。XTC を書き出せません。");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _spool.Flush();
        _spool.Seek(0, SeekOrigin.Begin);

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1 << 16);
        using var writer = new XtcWriter(output, _pages.Count, readDirection, leaveOpen: true);

        var buffer = new byte[_pages.Max(p => p.Size)];
        foreach (var (size, width, height) in _pages)
        {
            _spool.ReadExactly(buffer, 0, size);
            writer.WritePage(buffer.AsSpan(0, size), width, height);
        }

        writer.Complete();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _spool.Dispose();
        // DeleteOnClose で消えるはずだが、念のため後始末する。
        try { if (File.Exists(_spoolPath)) File.Delete(_spoolPath); } catch { /* 一時ファイルなので無視 */ }
    }
}
