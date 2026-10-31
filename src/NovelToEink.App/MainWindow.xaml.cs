using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovelToEink.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // ---- タイトル編集（ダブルクリックで TextBox 切り替え） ----
    private void Title_MouseClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not LibraryItemVm vm) return;
        vm.StartTitleEdit();
        var grid = VisualTreeHelper.GetParent(fe) as Grid;
        var box = grid?.Children.OfType<TextBox>().FirstOrDefault();
        if (box == null) return;
        box.Dispatcher.BeginInvoke(new Action(() =>
        {
            box.Focus();
            box.SelectAll();
        }));
    }

    private void TitleEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not LibraryItemVm vm) return;
        if (e.Key == Key.Enter)
        {
            vm.CommitTitleEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelTitleEdit();
            e.Handled = true;
        }
    }

    private void TitleEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not LibraryItemVm vm) return;
        vm.CommitTitleEdit();
    }
}
