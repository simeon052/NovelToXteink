using System.Diagnostics;

namespace NovelToEink.Core;

/// <summary>
/// Core 層のログ出力口。Core は UI を知らないので、既定は <see cref="Trace"/> へ書くだけ。
/// アプリ側が <see cref="Sink"/> を差し替えれば、ステータス表示やファイルへも流せる。
/// </summary>
public static class CoreLog
{
    /// <summary>ログの出力先。(レベル, メッセージ, 例外)。差し替えない場合は Trace へ出す。</summary>
    public static Action<string, string, Exception?> Sink { get; set; } = TraceSink;

    /// <summary>失敗しても処理は続けられるが、気づけるようにしたい出来事。</summary>
    public static void Warn(string message, Exception? ex = null) => Sink("WARN", message, ex);

    /// <summary>処理が成立しなかった出来事。</summary>
    public static void Error(string message, Exception? ex = null) => Sink("ERROR", message, ex);

    /// <summary>
    /// 失敗を握りつぶしてよい場面（キャッシュ・一時ファイル掃除など）で使う。
    /// 例外は捨てるが、原因を追えるように Trace にだけ残す。
    /// </summary>
    public static void Ignored(string what, Exception ex) => Trace.WriteLine($"[IGNORED] {what}: {ex.GetType().Name}: {ex.Message}");

    private static void TraceSink(string level, string message, Exception? ex)
        => Trace.WriteLine(ex is null ? $"[{level}] {message}" : $"[{level}] {message}: {ex.GetType().Name}: {ex.Message}");
}
