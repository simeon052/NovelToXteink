using System.Text.Json;

namespace NovelToEink.Core;

/// <summary>
/// JSON 設定・データファイルの読み込みを安全に行う。
/// ファイルが壊れていたときは、黙って空の値で始めて次の保存で上書きする代わりに、
/// 壊れたファイルを <c>*.bad</c> に退避してから既定値で始める（元のデータを失わない）。
/// </summary>
public static class JsonFileStore
{
    /// <summary>
    /// JSON を読む。ファイルが無ければ <paramref name="fallback"/>。
    /// 読めない（JSON が壊れている・I/O 失敗）ときは退避してログに残し、<paramref name="fallback"/> を返す。
    /// </summary>
    public static T Load<T>(string path, JsonSerializerOptions options, Func<T> fallback)
    {
        if (!File.Exists(path)) return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) ?? fallback();
        }
        catch (JsonException ex)
        {
            var moved = QuarantineCorruptFile(path);
            CoreLog.Error($"JSON が壊れています: {path}（{(moved is null ? "退避に失敗" : $"{Path.GetFileName(moved)} に退避")}）", ex);
            return fallback();
        }
        catch (IOException ex)
        {
            // 一時的な読み取り失敗（他プロセスが開いている等）。ファイルは壊れていないので退避しない。
            CoreLog.Error($"ファイルを読めません: {path}", ex);
            return fallback();
        }
        catch (UnauthorizedAccessException ex)
        {
            CoreLog.Error($"ファイルへのアクセスが拒否されました: {path}", ex);
            return fallback();
        }
    }

    /// <summary>壊れたファイルを <c>名前.bad[-連番]</c> に移す。移せたパスを返す（失敗時は null）。</summary>
    public static string? QuarantineCorruptFile(string path)
    {
        try
        {
            var dest = path + ".bad";
            for (var i = 1; File.Exists(dest); i++) dest = $"{path}.bad-{i}";
            File.Move(path, dest);
            return dest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoreLog.Warn($"壊れたファイルを退避できません: {path}", ex);
            return null;
        }
    }
}
