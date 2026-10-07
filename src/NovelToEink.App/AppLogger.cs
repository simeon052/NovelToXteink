using System.Diagnostics;

namespace NovelToEink.App;

/// <summary>
/// ILogger の実装。StatusText への反映と Trace デバッグ出力を行う。
/// </summary>
public sealed class AppLogger : ILogger
{
    private readonly Action<string?> _setStatus;

    /// <summary>
    /// AppLogger を作成する。
    /// </summary>
    /// <param name="setStatus">StatusText 更新用コールバック。</param>
    public AppLogger(Action<string?> setStatus)
    {
        _setStatus = setStatus;
    }

    public void Debug(string message)
    {
        Trace.WriteLine($"[DEBUG] {message}");
    }

    public void Info(string message)
    {
        Trace.WriteLine($"[INFO] {message}");
        _setStatus(message);
    }

    public void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message}: {ex.Message}" : message;
        Trace.WriteLine($"[ERROR] {fullMessage}");
        _setStatus(fullMessage);
    }

    public void Warning(string message)
    {
        Trace.WriteLine($"[WARN] {message}");
        _setStatus(message);
    }
}
