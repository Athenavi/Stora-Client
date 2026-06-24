using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace StoraDesktop.Views;

public sealed partial class SharePage : Page
{
    public ShareViewModel VM { get; }

    public SharePage(ShareViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) => await VM.LoadAsync();
    }

    private async void OnRefresh(object s, RoutedEventArgs e) => await VM.LoadAsync();

    private void OnCopyLink(object s, DoubleTappedRoutedEventArgs e)
    {
        var share = (e.OriginalSource as FrameworkElement)?.DataContext as ShareItem;
        if (share == null) return;
        CopyToClipboard(share);
    }

    private void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        var share = (e.OriginalSource as FrameworkElement)?.DataContext as ShareItem;
        if (share == null) return;

        var menu = new MenuFlyout();
        var copyItem = new MenuFlyoutItem { Text = "Copy Link" };
        copyItem.Click += (s2, e2) => CopyToClipboard(share);
        menu.Items.Add(copyItem);

        var delItem = new MenuFlyoutItem { Text = "Delete Share" };
        delItem.Click += async (s2, e2) => { await VM.DeleteShareCommand.ExecuteAsync(share); };
        menu.Items.Add(delItem);

        menu.ShowAt(ShareList, e.GetPosition(ShareList));
    }

    private static void CopyToClipboard(ShareItem share)
    {
        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pkg.SetText(share.ShareUrl);
        Clipboard.SetContent(pkg);
    }
}
