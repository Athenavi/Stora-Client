using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;

namespace StoraDesktop.Views;

public sealed partial class FavoritePage : Page
{
    public FavoriteViewModel VM { get; }

    public FavoritePage(FavoriteViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) => await VM.LoadAsync();
    }

    private async void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem f)
            await VM.RemoveFavoriteCommand.ExecuteAsync(f);
    }
}
