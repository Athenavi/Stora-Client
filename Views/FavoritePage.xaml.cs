using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;

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

    private async void OnRefresh(object s, RoutedEventArgs e) => await VM.LoadAsync();

    private async void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        var f = (e.OriginalSource as FrameworkElement)?.DataContext as FileItem;
        if (f == null) return;
        var menu = new MenuFlyout();
        var removeItem = new MenuFlyoutItem { Text = "Remove Favorite" };
        removeItem.Click += async (s2, e2) => await VM.RemoveFavoriteCommand.ExecuteAsync(f);
        menu.Items.Add(removeItem);
        menu.ShowAt(s as ListView, e.GetPosition(s as ListView));
    }
}
