using System.Text;
using NovelToEink.Core;

Console.OutputEncoding = Encoding.UTF8;

// 使い方:
//   NovelToEink.Cli <URL> [出力epubパス] [--max N] [--yoko] [--no-images] [--gray-off] [--cover official|generated|none|<index>] [--list]
// 例:
//   NovelToEink.Cli https://ncode.syosetu.com/n2267be/ out.epub --max 3

if (args.Length == 0)
{
    Console.WriteLine("usage: NovelToEink.Cli <URL> [output.epub] [--max N] [--yoko] [--no-images] [--gray-off] [--cover official|generated|none|<index>] [--list]");
    return 1;
}

// 隠しモード: カクヨムのWorkエンティティのフィールド名を調べる
if (args[0] == "kakfields")
{
    var html = await new HttpFetcher(800).GetStringAsync(args[1]);
    var m = System.Text.RegularExpressions.Regex.Match(html,
        "<script id=\"__NEXT_DATA__\"[^>]*>(.*?)</script>",
        System.Text.RegularExpressions.RegexOptions.Singleline);
    if (!m.Success) { Console.WriteLine("no __NEXT_DATA__"); return 1; }
    using var doc = System.Text.Json.JsonDocument.Parse(m.Groups[1].Value);
    var state = doc.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("__APOLLO_STATE__");
    foreach (var prop in state.EnumerateObject())
    {
        if (!prop.Name.StartsWith("Work:")) continue;
        Console.WriteLine($"### {prop.Name}");
        foreach (var f in prop.Value.EnumerateObject())
        {
            var v = f.Value.ValueKind == System.Text.Json.JsonValueKind.String ? f.Value.GetString() : f.Value.ToString();
            if (v != null && v.Length > 70) v = v[..70] + "…";
            Console.WriteLine($"  {f.Name} [{f.Value.ValueKind}] = {v}");
        }
        break;
    }
    return 0;
}

// ヘッドレス一括変換＋DB登録。失敗時はWindowsタスクスケジューラで自動再試行。
//   NovelToEink.Cli batch <folder> <url1> <url2> ...
if (args[0] == "batch")
{
    return await Batch(args);
}

// 隠しモード: ライブラリ機能のヘッドレス検証
//   NovelToEink.Cli libtest <folder> <url> [maxEpisodes] [episodesPerFile]
if (args[0] == "libtest")
{
    return await LibTest(args);
}

// 縦書きパッチ: 既存EPUBを再ダウンロードせず縦書き設定に差し替える。
//   NovelToEink.Cli patch [folder]   (folder省略時は settings.json の OutputFolder を使用)
if (args[0] == "patch")
{
    return PatchVertical(args);
}

var url = args[0];
string? output = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
var maxEpisodes = GetIntOpt("--max", 0);
var listOnly = HasOpt("--list");
var coverChoice = GetStrOpt("--cover", "auto");

var epubOpt = new EpubOptions
{
    WritingMode = HasOpt("--yoko") ? WritingMode.Horizontal : WritingMode.Vertical,
    IncludeInlineImages = !HasOpt("--no-images"),
    GrayscaleImages = !HasOpt("--gray-off"),
};
var dlOpt = new DownloadOptions { MaxEpisodes = maxEpisodes };

using var service = new NovelDownloadService(dlOpt);
if (!service.IsSupported(url))
{
    Console.Error.WriteLine("対応していないURLです（なろう / カクヨムのみ）。");
    return 2;
}

var progress = new Progress<DownloadProgress>(p =>
{
    var loc = p.Total > 0 ? $" ({p.Current}/{p.Total})" : "";
    Console.WriteLine($"[{p.Phase}]{loc} {p.Message}");
});

try
{
    if (listOnly)
    {
        var meta = await service.GetMetadataAsync(url);
        Console.WriteLine($"タイトル : {meta.Title}");
        Console.WriteLine($"作者     : {meta.Author}");
        Console.WriteLine($"サイト   : {meta.Site}  ID: {meta.WorkId}");
        if (meta.OfficialCoverUrl != null) Console.WriteLine($"公式表紙 : {meta.OfficialCoverUrl}");
        Console.WriteLine($"あらすじ : {Trunc(meta.Description, 120)}");
        Console.WriteLine("--- 目次 ---");
        var toc = await service.GetTableOfContentsAsync(url);
        string? chap = null;
        foreach (var e in toc)
        {
            if (e.ChapterTitle != chap) { chap = e.ChapterTitle; if (chap != null) Console.WriteLine($"■ {chap}"); }
            Console.WriteLine($"  {e.Index,4}. {e.Title}");
        }
        Console.WriteLine($"合計 {toc.Count} 話");
        return 0;
    }

    var novel = await service.DownloadAsync(url, epubOpt, dlOpt, progress);
    Console.WriteLine($"取得完了: {novel.Episodes.Count}話 / 画像 {novel.Images.Count}枚");

    // 表紙決定
    ScrapedImage? cover = coverChoice switch
    {
        "none" => null,
        "generated" => ImageProcessor.GenerateTextCover(novel.Metadata.Title, novel.Metadata.Author, epubOpt),
        "official" => novel.Images.FirstOrDefault(i => i.IsOfficialCover),
        _ when int.TryParse(coverChoice, out var ci) && ci >= 0 && ci < novel.Images.Count => novel.Images[ci],
        _ => novel.Images.FirstOrDefault(i => i.IsOfficialCover)
             ?? novel.Images.FirstOrDefault()
             ?? ImageProcessor.GenerateTextCover(novel.Metadata.Title, novel.Metadata.Author, epubOpt),
    };

    output ??= SanitizeFileName(novel.Metadata.Title) + ".epub";
    var buildProgress = new Progress<string>(m => Console.WriteLine($"[EPUB] {m}"));
    var result = EpubBuilder.Build(novel, cover, epubOpt, output, buildProgress);
    Console.WriteLine($"生成: {result.OutputPath}  ({result.EpisodeCount}話, 画像{result.ImageCount}枚, {result.SizeBytes / 1024.0:F0} KB)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("エラー: " + ex.Message);
    return 3;
}

bool HasOpt(string name) => args.Contains(name);
int GetIntOpt(string name, int def)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : def;
}
string GetStrOpt(string name, string def)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : def;
}
static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
static string SanitizeFileName(string s)
{
    foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
    return s.Trim();
}

static async Task<int> LibTest(string[] a)
{
    if (a.Length < 3) { Console.Error.WriteLine("usage: libtest <folder> <url> [maxEpisodes] [episodesPerFile]"); return 1; }
    var folder = a[1];
    var url = a[2];
    var max = a.Length > 3 && int.TryParse(a[3], out var m) ? m : 2;
    var per = a.Length > 4 && int.TryParse(a[4], out var pp) ? pp : 200;

    var settings = new NovelToEink.Core.AppSettings { OutputFolder = folder, RequestDelayMs = 1200, EpisodesPerFile = per };
    var svc = new NovelToEink.Core.LibraryService(settings);
    var opt = settings.ToEpubOptions();
    var prog = new Progress<NovelToEink.Core.DownloadProgress>(p =>
        Console.WriteLine($"  [{p.Phase}] {(p.Total > 0 ? $"{p.Current}/{p.Total} " : "")}{p.Message}"));

    Console.WriteLine($"=== 1) ダウンロード（max {max}話） ===");
    var novel = await svc.DownloadAsync(url, opt, prog, default, max);
    var cover = novel.Images.FirstOrDefault(i => i.IsOfficialCover)
                ?? NovelToEink.Core.ImageProcessor.GenerateTextCover(novel.Metadata.Title, novel.Metadata.Author, opt);

    Console.WriteLine($"=== 2) EPUB生成＋ライブラリ登録（{per}話/ファイルで分割） ===");
    var entry = svc.BuildAndRegister(novel, cover, opt, new Progress<string>(s => Console.WriteLine("  " + s)));
    Console.WriteLine($"  title={entry.Title}  author={entry.Author}  episodes={entry.EpisodeCount}  completed={entry.IsCompleted}  siteLastUp={entry.SiteLastUpdated:yyyy-MM-dd}");
    Console.WriteLine($"  parts ({entry.EpubParts.Count}):");
    foreach (var p in entry.EpubParts) Console.WriteLine($"    [{File.Exists(p)}] {Path.GetFileName(p)}");
    Console.WriteLine($"  cover exists: {entry.CoverImagePath != null && File.Exists(entry.CoverImagePath)}");
    Console.WriteLine($"  library.json exists: {File.Exists(Path.Combine(folder, "library.json"))}");

    Console.WriteLine("=== 3) 別インスタンスで再読込（永続化確認） ===");
    var svc2 = new NovelToEink.Core.LibraryService(settings);
    Console.WriteLine($"  loaded entries: {svc2.Entries.Count}  first: {svc2.Entries.FirstOrDefault()?.Title}");

    Console.WriteLine("=== 4) 更新チェック（CheckAsync 経由・ネットワーク） ===");
    var e = svc2.Entries.First();
    e.EpisodeCount = max - 1;            // 実際より1話少ない = 更新あり扱いになるはず
    e.LastEpisodeTitle = "__stale__";
    var has1 = await svc2.CheckAsync(e, default);
    Console.WriteLine($"  stale -> CheckAsync hasUpdate = {has1} (expected True), status={e.Status}");

    Console.WriteLine("=== 5) HasUpdate ロジック検証（実TOCと比較） ===");
    using var nds = new NovelToEink.Core.NovelDownloadService(
        new NovelToEink.Core.DownloadOptions { RequestDelayMs = 1200 });
    var toc = await nds.GetTableOfContentsAsync(e.Url);
    e.EpisodeCount = toc.Count; e.LastEpisodeTitle = toc[^1].Title;
    Console.WriteLine($"  最新一致 -> HasUpdate = {e.HasUpdate(toc)} (expected False)");
    e.EpisodeCount = toc.Count - 1;
    Console.WriteLine($"  話数不足 -> HasUpdate = {e.HasUpdate(toc)} (expected True)");
    e.EpisodeCount = toc.Count; e.LastEpisodeTitle = "__changed__";
    Console.WriteLine($"  最終話変化 -> HasUpdate = {e.HasUpdate(toc)} (expected True)");

    Console.WriteLine("=== 完了 ===");
    return 0;
}

static async Task<int> Batch(string[] a)
{
    if (a.Length < 3) { Console.Error.WriteLine("usage: batch <folder> <url1> [url2 ...]"); return 1; }
    var folder = a[1];
    var urls = a.Skip(2).Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();

    // 保存先を設定に反映（GUIが次回このフォルダを開く）
    var settings = NovelToEink.Core.AppSettings.Load();
    settings.OutputFolder = folder;
    settings.Save();
    var svc = new NovelToEink.Core.LibraryService(settings);
    var opt = settings.ToEpubOptions();

    Console.WriteLine($"=== バッチ変換開始 : {urls.Count} 件 -> {folder}  ({DateTime.Now:HH:mm:ss}) ===");
    var failed = new List<string>();

    foreach (var url in urls)
    {
        // 既に登録済みでファイルが揃っていればスキップ（再試行時の冪等性）
        var done = svc.Entries.FirstOrDefault(e => e.Url == url);
        if (done != null && done.EpisodeCount > 0 && done.EpubParts.Count > 0 && done.EpubParts.All(File.Exists))
        {
            Console.WriteLine($"[skip] 取得済み: {done.Title} ({done.EpisodeCount}話, {done.EpubParts.Count}分割)");
            continue;
        }

        try
        {
            Console.WriteLine($"[get ] {url}");
            var prog = new Progress<NovelToEink.Core.DownloadProgress>(p =>
            {
                if (p.Total > 0 && p.Current % 50 == 0)
                    Console.WriteLine($"        {p.Phase} {p.Current}/{p.Total}");
            });
            var novel = await svc.DownloadAsync(url, opt, prog, default);
            var cover = novel.Images.FirstOrDefault(i => i.IsOfficialCover)
                        ?? NovelToEink.Core.ImageProcessor.GenerateTextCover(novel.Metadata.Title, novel.Metadata.Author, opt);
            var entry = svc.BuildAndRegister(novel, cover, opt, null);
            Console.WriteLine($"[done] {entry.Title}  {entry.EpisodeCount}話 / {entry.EpubParts.Count}分割 / 完結={entry.IsCompleted} / 最終更新={entry.SiteLastUpdated:yyyy-MM-dd}");
        }
        catch (Exception ex)
        {
            failed.Add(url);
            Console.Error.WriteLine($"[FAIL] {url} -> {ex.Message}");
        }
    }

    if (failed.Count == 0)
    {
        Console.WriteLine("=== 全件完了。再試行タスクを解除します。 ===");
        RunPowerShell("Unregister-ScheduledTask -TaskName 'NovelToEink_Batch_Retry' -Confirm:$false -ErrorAction SilentlyContinue");
        return 0;
    }

    Console.WriteLine($"=== {failed.Count} 件が失敗。90分後に自動再試行をスケジュールします。 ===");
    ScheduleRetry(folder, urls);
    return 0;
}

static void ScheduleRetry(string folder, List<string> urls)
{
    var exe = Environment.ProcessPath ?? "";
    if (string.IsNullOrEmpty(exe) || exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("  自己再スケジュール不可（単一exeとして実行してください）。");
        return;
    }
    // -Argument 文字列を組み立て（パスはダブルクオート、PS側はシングルクオートでリテラル）
    var argLine = "batch \"" + folder + "\" " + string.Join(" ", urls);
    var script =
        $"$a = New-ScheduledTaskAction -Execute '{exe}' -Argument '{argLine.Replace("'", "''")}';" +
        $"$t = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(90);" +
        $"Register-ScheduledTask -TaskName 'NovelToEink_Batch_Retry' -Action $a -Trigger $t -Force | Out-Null;" +
        $"Write-Output 'scheduled'";
    var ok = RunPowerShell(script);
    Console.WriteLine(ok ? "  再試行タスクを登録しました（NovelToEink_Batch_Retry）。" : "  再試行タスクの登録に失敗しました。");
}

static int PatchVertical(string[] a)
{
    var settings = NovelToEink.Core.AppSettings.Load();
    if (a.Length > 1) settings.OutputFolder = a[1];
    var folder = settings.OutputFolder;

    Console.WriteLine($"=== 縦書きパッチ : {folder} ===");
    if (!Directory.Exists(folder)) { Console.Error.WriteLine($"フォルダが存在しません: {folder}"); return 1; }

    var svc = new NovelToEink.Core.LibraryService(settings);
    if (svc.Entries.Count == 0) { Console.WriteLine("ライブラリにエントリがありません。"); return 0; }

    var patched = 0;
    var errors = 0;
    svc.PatchAllVertical(new Progress<string>(msg =>
    {
        Console.WriteLine(msg.StartsWith("パッチ中:") ? $"  {msg}" : $"  [!] {msg}");
        if (msg.StartsWith("パッチ中:")) patched++;
        else if (msg.StartsWith("スキップ:")) errors++;
    }));
    Console.WriteLine($"=== 完了: {patched} ファイルを縦書きにパッチ。エラー: {errors} ===");
    return 0;
}

static bool RunPowerShell(string script)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(20000);
        return p.ExitCode == 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("  PowerShell実行失敗: " + ex.Message);
        return false;
    }
}
