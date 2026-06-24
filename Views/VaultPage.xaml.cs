using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;
using System.Threading.Tasks;

namespace StoraDesktop.Views;

public sealed partial class VaultPage : Page
{
    public VaultViewModel VM { get; }

    public VaultPage(VaultViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) => await VM.LoadAsync();
    }

    private async void OnRefresh(object s, RoutedEventArgs e) => await VM.LoadAsync();

    private void OnVaultDoubleTap(object s, DoubleTappedRoutedEventArgs e)
    {
        var v = (e.OriginalSource as FrameworkElement)?.DataContext as VaultItem;
        if (v != null) VM.ShowUnlockCommand.Execute(v);
    }

    private void OnUnlockClick(object s, RoutedEventArgs e)
    {
        var id = (s as Button)?.Tag;
        if (id is long vid)
        {
            var v = new VaultItem { Id = vid };
            foreach (var item in VM.Vaults)
            {
                if (item.Id == vid) { v = item; break; }
            }
            VM.ShowUnlockCommand.Execute(v);
        }
    }

    private async void OnVaultRightTap(object s, RightTappedRoutedEventArgs e)
    {
        var v = (e.OriginalSource as FrameworkElement)?.DataContext as VaultItem;
        if (v == null) return;
        var menu = new MenuFlyout();
        var delItem = new MenuFlyoutItem { Text = "Delete Vault" };
        delItem.Click += async (s2, e2) => await VM.DeleteVaultCommand.ExecuteAsync(v);
        menu.Items.Add(delItem);
        menu.ShowAt(s as ListView, e.GetPosition(s as ListView));
    }

    private void OnBackToList(object s, RoutedEventArgs e) => VM.BackToListCommand.Execute(null);
    private void OnLock(object s, RoutedEventArgs e) => VM.LockCommand.Execute(null);

    private async void OnFileRightTap(object s, RightTappedRoutedEventArgs e)
    {
        var f = (e.OriginalSource as FrameworkElement)?.DataContext as VaultFileItem;
        if (f == null) return;
        var menu = new MenuFlyout();
        var dlItem = new MenuFlyoutItem { Text = "Download" };
        dlItem.Click += async (s2, e2) => await VM.DownloadFileCommand.ExecuteAsync(f);
        menu.Items.Add(dlItem);
        var delItem = new MenuFlyoutItem { Text = "Delete" };
        delItem.Click += async (s2, e2) => await VM.DeleteFileCommand.ExecuteAsync(f);
        menu.Items.Add(delItem);
        menu.ShowAt(s as ListView, e.GetPosition(s as ListView));
    }
}
