using System.Buffers.Binary;

namespace NovelToEink.Xtc;

/// <summary>ページの読み進む向き。</summary>
public enum XtcReadDirection : byte
{
    /// <summary>左→右（横組み）。</summary>
    LeftToRight = 0,

    /// <summary>右→左（日本語の縦組み）。</summary>
    RightToLeft = 1,
}

/// <summary>
/// XTC コンテナ（複数 XTG ページ）の書き出し。ページを 1 枚ずつ受け取り、逐次ストリームへ書く。
/// </summary>
/// <remarks>
/// 48 バイトヘッダー + pageCount×16 バイトのインデックステーブル + XTG データエリア。
/// 実機で読めるサンプル XTC および epub2xtc の実装と一致するレイアウト。
/// gist 仕様書には末尾に chapterOffset を加えた 56 バイト版の記載があるが、
/// 章機能は Xteink ファームウェア 3.1.0 時点で未実装であり、実ファイルは 48 バイトを使う。
/// </remarks>
public sealed class XtcWriter : IDisposable
{
    /// <summary>XTC ヘッダーのバイト数。</summary>
    public const int HeaderSize = 48;

    /// <summary>ページインデックス 1 件のバイト数。</summary>
    public const int IndexEntrySize = 16;

    /// <summary>XTC のマジックナンバー（0x00435458 = "XTC\0"）。</summary>
    public const uint Mark = 0x00435458;

    /// <summary>フォーマットバージョン。</summary>
    public const ushort Version = 1;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly int _pageCount;
    private readonly XtcReadDirection _readDirection;
    private readonly byte[] _index;

    private int _written;
    private long _nextOffset;
    private bool _disposed;

    /// <summary>これまでに書き込んだページ数。</summary>
    public int PagesWritten => _written;

    /// <param name="stream">出力先。シーク可能である必要はない。</param>
    /// <param name="pageCount">総ページ数。ヘッダーとインデックスを先に確定させるため事前に必要。</param>
    /// <param name="readDirection">読み進む向き。縦組みは <see cref="XtcReadDirection.RightToLeft"/>。</param>
    /// <param name="leaveOpen">true なら <see cref="Dispose"/> でストリームを閉じない。</param>
    public XtcWriter(Stream stream, int pageCount,
        XtcReadDirection readDirection = XtcReadDirection.RightToLeft, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (pageCount <= 0 || pageCount > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pageCount),
                $"ページ数は 1〜{ushort.MaxValue} の範囲である必要があります（指定値 {pageCount}）。");

        _stream = stream;
        _leaveOpen = leaveOpen;
        _pageCount = pageCount;
        _readDirection = readDirection;
        _index = new byte[pageCount * IndexEntrySize];
        _nextOffset = HeaderSize + _index.Length;

        // ヘッダーとインデックス領域を先に確保しておき、全ページ書き込み後に埋め戻す。
        _stream.Write(new byte[HeaderSize]);
        _stream.Write(_index);
    }

    /// <summary>XTG ブロックを 1 ページ追記する。</summary>
    /// <param name="xtgBlob"><see cref="XtgWriter"/> が生成した XTG ブロック。</param>
    /// <param name="width">ページ幅（px）。</param>
    /// <param name="height">ページ高さ（px）。</param>
    public void WritePage(ReadOnlySpan<byte> xtgBlob, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_written >= _pageCount)
            throw new InvalidOperationException($"宣言したページ数 {_pageCount} を超えて書き込もうとしました。");

        var entry = _index.AsSpan(_written * IndexEntrySize, IndexEntrySize);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[..8], (ulong)_nextOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)xtgBlob.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(12, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(14, 2), (ushort)height);

        _stream.Write(xtgBlob);
        _nextOffset += xtgBlob.Length;
        _written++;
    }

    /// <summary>ヘッダーとインデックステーブルを確定させる。ストリームはシーク可能である必要がある。</summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_written != _pageCount)
            throw new InvalidOperationException($"ページ数が一致しません。宣言 {_pageCount}、実書き込み {_written}。");

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Mark);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)_pageCount);
        header[8] = (byte)_readDirection;
        header[9] = 0;  // hasMetadata
        header[10] = 0; // hasThumbnails
        header[11] = 0; // hasChapters（ファームウェア未実装のため常に 0）
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), 0); // currentPage
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16, 8), 0); // metadataOffset
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24, 8), HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32, 8), (ulong)(HeaderSize + _index.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40, 8), 0); // thumbOffset

        _stream.Flush();
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.Write(header);
        _stream.Write(_index);
        _stream.Flush();
        _stream.Seek(0, SeekOrigin.End);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_leaveOpen) _stream.Dispose();
    }
}
