using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;

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

    private async void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        var share = (e.OriginalSource as FrameworkElement)?.DataContext as ShareItem;
        if (share == null) return;

        var menu = new MenuFlyout();
        var copyItem = new MenuFlyoutItem { Text = "📋 复制链接", Tag = "copy" };
        copyItem.Click += async (s2, e2) => { VM.CopyLinkCommand.Execute(share); };
        var delItem = new MenuFlyoutItem { Text = "🗑 删除分享", Tag = "delete" };
        delItem.Click += async (s2, e2) => { await VM.DeleteShareCommand.ExecuteAsync(share); };

        menu.Items.Add(copyItem);
        menu.Items.Add(delItem);
        menu.ShowAt(ShareList, e.GetPosition(ShareList));
    }
}
