using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NovelToEink.App;

/// <summary>件数が 0 のとき Visible（空状態メッセージ表示用）。</summary>
public sealed class CountToVisibility : IValueConverter
{
    public static readonly CountToVisibility Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
