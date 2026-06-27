using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>ライブラリ一覧の1行。LibraryEntry を表示用にラップする。</summary>
public sealed class LibraryItemVm : ViewModelBase
{
    public LibraryEntry Entry { get; }

    public LibraryItemVm(LibraryEntry entry)
    {
        Entry = entry;
        _nameTemplateEdit = entry.NameTemplate;
        LoadThumbnail();
    }

    public string Title => Entry.Title;
    public string Author => string.IsNullOrWhiteSpace(Entry.Author) ? "(作者不明)" : Entry.Author;
    public string SiteLabel => Entry.Site switch
    {
        NovelSite.Syosetu => "なろう",
        NovelSite.Kakuyomu => "カクヨム",
        _ => Entry.Site.ToString(),
    };
    public string EpisodesText => $"{Entry.EpisodeCount} 話";
    public string Url => Entry.Url;

    public string PartsText => Entry.EpubParts.Count > 1 ? $"{Entry.EpubParts.Count} 分割" : "1 ファイル";

    public bool IsCompleted => Entry.IsCompleted;
    public Visibility CompletedVisibility => Entry.IsCompleted ? Visibility.Visible : Visibility.Collapsed;

    public string SiteUpdatedText => Entry.SiteLastUpdated is { } d ? $"サイト最終更新: {d:yyyy/MM/dd}" : "";

    public string LastUpdatedText => Entry.LastUpdatedAt is { } d ? $"変換: {d:yyyy/MM/dd HH:mm}" : "変換: -";
    public string LastCheckedText => Entry.LastCheckedAt is { } d ? $"確認: {d:yyyy/MM/dd HH:mm}" : "確認: -";

    private string _nameTemplateEdit;
    /// <summary>編集中のファイル名テンプレート（「改名」で確定）。</summary>
    public string NameTemplateEdit { get => _nameTemplateEdit; set => Set(ref _nameTemplateEdit, value); }

    public string StatusText => _isBusy ? _busyText : Entry.Status switch
    {
        UpdateStatus.UpToDate => "最新",
        UpdateStatus.UpdateAvailable => "更新あり",
        UpdateStatus.Error => "エラー",
        _ => "未チェック",
    };

    public Brush StatusBrush => _isBusy ? Brushes.SteelBlue : Entry.Status switch
    {
        UpdateStatus.UpToDate => (Brush)new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),     // green
        UpdateStatus.UpdateAvailable => new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),      // orange
        UpdateStatus.Error => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),                // red
        _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),                                 // gray
    };

    public bool HasUpdate => !_isBusy && Entry.Status == UpdateStatus.UpdateAvailable;

    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail { get => _thumbnail; private set => Set(ref _thumbnail, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (Set(ref _isBusy, value)) RefreshStatus(); }
    }

    private string _busyText = "";
    public string BusyText
    {
        get => _busyText;
        set { if (Set(ref _busyText, value) && _isBusy) RefreshStatus(); }
    }

    /// <summary>エントリ更新後に表示を再評価する。</summary>
    public void Refresh()
    {
        OnChanged(nameof(Title));
        OnChanged(nameof(Author));
        OnChanged(nameof(EpisodesText));
        OnChanged(nameof(PartsText));
        OnChanged(nameof(IsCompleted));
        OnChanged(nameof(CompletedVisibility));
        OnChanged(nameof(SiteUpdatedText));
        OnChanged(nameof(LastUpdatedText));
        OnChanged(nameof(LastCheckedText));
        NameTemplateEdit = Entry.NameTemplate;
        RefreshStatus();
        LoadThumbnail();
    }

    private void RefreshStatus()
    {
        OnChanged(nameof(StatusText));
        OnChanged(nameof(StatusBrush));
        OnChanged(nameof(HasUpdate));
    }

    public void LoadThumbnail()
    {
        try
        {
            if (Entry.CoverImagePath is { } p && File.Exists(p))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.DecodePixelWidth = 96;
                bmp.UriSource = new Uri(p);
                bmp.EndInit();
                bmp.Freeze();
                Thumbnail = bmp;
            }
            else Thumbnail = null;
        }
        catch { Thumbnail = null; }
    }
}
