using System.Windows;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Core のログ（CoreLog）は既定では Trace に出るだけ。ERROR はユーザーにも見えるよう、
        // 最前面のウィンドウのステータス表示へも流す（WARN は多いので Trace のみ）。
        CoreLog.Sink = (level, message, ex) =>
        {
            var text = ex is null ? message : $"{message}: {ex.Message}";
            System.Diagnostics.Trace.WriteLine($"[{level}] {text}");
            if (level == "ERROR" && Current?.MainWindow?.DataContext is MainViewModel vm)
                Current.Dispatcher.BeginInvoke(() => vm.StatusText = text);
        };
        // 言語: 既定はOSのUICulture。settings.json の Language（auto/ja/en）で上書き。
        Locale.Override = AppSettings.Load().Language;
        Locale.Changed += () => Current?.Dispatcher?.BeginInvoke(LocRegistry.Refresh);
        // レビューM-2: UIスレッドでの未処理例外でクラッシュしない（ログ＋ステータス表示）。
        DispatcherUnhandledException += (s, e) =>
        {
            CoreLog.Error(Locale.T("unhandled_exception"), e.Exception);
            e.Handled = true;
        };
        base.OnStartup(e);
    }
}

