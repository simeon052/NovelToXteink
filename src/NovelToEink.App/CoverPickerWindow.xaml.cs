using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NovelToEink.Core;

namespace NovelToEink.App;

public partial class CoverPickerWindow : Window
{
    public ObservableCollection<CoverCandidate> Covers { get; } = [];

    /// <summary>OKで確定した表紙。null は「表紙なし」。</summary>
    public CoverCandidate? Result { get; private set; }

    private readonly EpubOptions _options;
    private readonly NovelMetadata _metadata;
    private readonly CancellationTokenSource _searchCts = new();

    /// <param name="novel">対象作品のダウンロード結果。</param>
    /// <param name="options">EPUBオプション（プレビューの見た目に使う）。</param>
    /// <param name="prefetched">
    /// 追加時に裏で集めておいた表紙候補。渡すと即座に一覧へ並び、画像検索をやり直さない。
    /// </param>
    public CoverPickerWindow(NovelDownload novel, EpubOptions options, IReadOnlyList<ScrapedImage>? prefetched = null)
    {
        _options = options;
        _metadata = novel.Metadata;

        InitializeComponent();
        TitleText.Text = novel.Metadata.Title;
        SubText.Text = Locale.T("cover_picker_info", novel.Episodes.Count, novel.Images.Count);

        BuildCandidates(novel, options);

        if (prefetched is { Count: > 0 })
        {
            InsertSearchResults(prefetched);
            SearchStatus.Text = Locale.T("prefetched_fmt", prefetched.Count);
        }
        else
        {
            SearchStatus.Text = Locale.T("SearchingMsg");
            Loaded += async (_, _) => await SearchAndAddAsync();
        }

        CoverList.ItemsSource = Covers;
        CoverList.SelectedItem = Covers.FirstOrDefault();

        Closed += (_, _) => _searchCts.Cancel();
    }

    /// <summary>検索結果を「表紙なし」の直前へ差し込む。</summary>
    private void InsertSearchResults(IReadOnlyList<ScrapedImage> images)
    {
        var insertAt = Math.Max(0, Covers.Count - 1);
        for (var i = 0; i < images.Count; i++)
        {
            var img = images[i];
            Covers.Insert(insertAt + i,
                CoverCandidate.FromImage(img, Locale.T("search_img_fmt", i + 1, img.Width, img.Height), _options));
        }
    }

    private void BuildCandidates(NovelDownload novel, EpubOptions options)
    {
        var official = novel.Images.FirstOrDefault(i => i.IsOfficialCover);
        if (official != null)
            Covers.Add(CoverCandidate.FromImage(official, Locale.T("official_cover"), options));

        // 文字生成表紙は必ず追加（フォント不備時は枠のみのフォールバック）
        ScrapedImage? genCover = null;
        try
        {
            genCover = ImageProcessor.GenerateTextCover(novel.Metadata.Title, novel.Metadata.Author, options);
        }
        catch (Exception ex)
        {
            // フォント不備などで文字表紙を作れない場合は、簡易表紙で代える
            CoreLog.Warn(Locale.T("text_cover_fail"), ex);
            try { genCover = ImageProcessor.GenerateMinimalCover(); }
            catch (Exception ex2) { CoreLog.Error(Locale.T("simple_cover_fail"), ex2); }
        }
        if (genCover != null)
            Covers.Add(CoverCandidate.FromImage(genCover, Locale.T("TextGenerated"), options));

        var n = 0;
        foreach (var img in novel.Images.Where(i => !i.IsOfficialCover))
        {
            n++;
            Covers.Add(CoverCandidate.FromImage(img, Locale.T("illus_fmt", n, img.EpisodeIndex, img.Width, img.Height), options));
        }

        Covers.Add(CoverCandidate.None());
    }

    private async Task SearchAndAddAsync()
    {
        try
        {
            var images = await ImageSearchService.SearchAsync(
                _metadata.Title, _metadata.Author, 5, _searchCts.Token);

            InsertSearchResults(images);

            SearchStatus.Text = images.Count > 0
                ? Locale.T("img_added_fmt", images.Count)
                : Locale.T("img_no_results");
        }
        catch (OperationCanceledException)
        {
            // ウィンドウを閉じた場合は何もしない
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            SearchStatus.Text = Locale.T("img_search_err_fmt", msg[..Math.Min(60, msg.Length)]);
        }
    }

    private void CoverList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => Result = CoverList.SelectedItem as CoverCandidate;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = CoverList.SelectedItem as CoverCandidate;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---- 手動追加（ファイル選択 / D&D / ペースト）----

    private void AddFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Locale.T("choose_cover_image"),
            Filter = Locale.T("image_filter"),
        };
        if (dlg.ShowDialog() != true) return;
        AddImageFromPath(dlg.FileName);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files)
            {
                if (AddImageFromPath(file)) break; // 最初の有効画像のみ
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            PasteFromClipboard();
    }

    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            if (files.Count > 0 && AddImageFromPath(files[0]!)) return;
        }
        if (Clipboard.ContainsImage())
        {
            var bmpSrc = Clipboard.GetImage();
            if (bmpSrc != null)
                AddImageFromBytes(BitmapSourceToJpeg(bmpSrc), Locale.T("clipboard"));
        }
    }

    private bool AddImageFromPath(string filePath)
    {
        try
        {
            return AddImageFromBytes(File.ReadAllBytes(filePath), Path.GetFileName(filePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoreLog.Warn(Locale.T("image_unreadable_fmt", filePath), ex);
            return false;
        }
    }

    private bool AddImageFromBytes(byte[] bytes, string label)
    {
        var img = ImageProcessor.TryLoad(bytes, $"file:{label}");
        if (img == null) return false;
        var candidate = CoverCandidate.FromImage(img, Locale.T("manual_fmt", label), _options);
        // "なし" の直前に挿入
        var insertAt = Math.Max(0, Covers.Count - 1);
        Covers.Insert(insertAt, candidate);
        CoverList.SelectedItem = candidate;
        SearchStatus.Text = Locale.T("manual_size_fmt", label, img.Width, img.Height);
        return true;
    }

    private static byte[] BitmapSourceToJpeg(BitmapSource src)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
