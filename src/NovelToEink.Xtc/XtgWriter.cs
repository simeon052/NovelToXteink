using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NovelToEink.Xtc;

/// <summary>
/// XTG（1bit モノクロ画像）ブロックの生成。
/// </summary>
/// <remarks>
/// レイアウトは実機で読める XTC サンプルの解析で確定したもの：
/// <list type="bullet">
///   <item>22 バイトヘッダー：mark(4) / width(2) / height(2) / colorMode(1) / compression(1) / dataSize(4) / md5(8)</item>
///   <item>ピクセルは行優先（上→下）、1 バイト 8px、MSB が左端</item>
///   <item>ビット 1 = 白、ビット 0 = 黒</item>
///   <item>md5 は「ピクセルデータのみ」の MD5 の先頭 8 バイト（ヘッダーは含めない）</item>
/// </list>
/// </remarks>
public static class XtgWriter
{
    /// <summary>XTG ヘッダーのバイト数。</summary>
    public const int HeaderSize = 22;

    /// <summary>XTG のマジックナンバー（0x00475458 = "XTG\0"）。</summary>
    public const uint Mark = 0x00475458;

    /// <summary>指定サイズの 1bpp ピクセルデータのバイト数を返す。</summary>
    public static int GetDataSize(int width, int height) => ((width + 7) / 8) * height;

    /// <summary>1 行あたりのバイト数（ストライド）を返す。</summary>
    public static int GetStride(int width) => (width + 7) / 8;

    /// <summary>
    /// 8bpp グレースケール画像を 1bpp にしきい値処理し、XTG ブロック（ヘッダー + データ）を生成する。
    /// </summary>
    /// <param name="gray">グレースケール画素（長さ <paramref name="width"/> × <paramref name="height"/>）。</param>
    /// <param name="width">画像幅（px）。</param>
    /// <param name="height">画像高さ（px）。</param>
    /// <param name="threshold">この値より大きい画素を白とする。</param>
    public static byte[] FromGrayscale(ReadOnlySpan<byte> gray, int width, int height, int threshold = 200)
    {
        if (width <= 0 || width > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));
        if (gray.Length < width * height)
            throw new ArgumentException("グレースケールデータが画像サイズより小さいです。", nameof(gray));

        var stride = GetStride(width);
        var data = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var srcRow = y * width;
            var dstRow = y * stride;
            for (var x = 0; x < width; x++)
            {
                // ビット 1 = 白。しきい値より明るい画素だけビットを立てる。
                if (gray[srcRow + x] > threshold)
                    data[dstRow + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
            }
        }

        return FromPackedBits(data, width, height);
    }

    /// <summary>
    /// パック済みの 1bpp データから XTG ブロックを生成する。
    /// </summary>
    public static byte[] FromPackedBits(ReadOnlySpan<byte> data, int width, int height)
    {
        var expected = GetDataSize(width, height);
        if (data.Length != expected)
            throw new ArgumentException($"1bpp データ長が不正です。期待値 {expected}、実際 {data.Length}。", nameof(data));

        var blob = new byte[HeaderSize + data.Length];
        var header = blob.AsSpan(0, HeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], Mark);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), (ushort)height);
        header[8] = 0; // colorMode: 0 = 1bit モノクロ
        header[9] = 0; // compression: 未実装のため常に 0
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(10, 4), (uint)data.Length);

        // md5 はピクセルデータのみを対象とし、先頭 8 バイトを格納する。
        Span<byte> digest = stackalloc byte[16];
        MD5.HashData(data, digest);
        digest[..8].CopyTo(header.Slice(14, 8));

        data.CopyTo(blob.AsSpan(HeaderSize));
        return blob;
    }
}
