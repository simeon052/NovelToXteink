namespace NovelToEink.App;

/// <summary>
/// アプリケーション全体のログ出力用のインターフェース。
/// WPF アプリでは StatusText への反映と Trace デバッグ出力を行う。
/// </summary>
public interface ILogger
{
    /// <summary>デバッグメッセージを記録する。</summary>
    void Debug(string message);

    /// <summary>情報メッセージを記録する。</summary>
    void Info(string message);

    /// <summary>エラーメッセージを記録する。</summary>
    void Error(string message, Exception? ex = null);

    /// <summary>警告メッセージを記録する。</summary>
    void Warning(string message);
}
