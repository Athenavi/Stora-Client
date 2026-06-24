using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StoraDesktop.Views;

public sealed partial class FilePage : Page
{
    public FileViewModel VM { get; }

    public FilePage(FileViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) =>
        {
            await VM.InitAsync();
            AttachScroll();
        };
    }

    private void AttachScroll()
    {
        var sv = FindChild<ScrollViewer>(FileListView);
        if (sv != null)
            sv.ViewChanged += (scrollSender, scrollArgs) =>
            {
                var scroller = (ScrollViewer)scrollSender;
                if (scroller.VerticalOffset > scroller.ScrollableHeight - 200 && VM.HasMore)
                    VM.LoadMoreCommand.Execute(null);
            };
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var c = VisualTreeHelper.GetChild(parent, i);
            if (c is T t) return t;
            var found = FindChild<T>(c);
            if (found != null) return found;
        }
        return null;
    }

    private async void OnUpload(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files == null) return;
        foreach (var f in files)
            await VM.ChunkedUploadAsync(f);
        await VM.InitAsync();
    }

    private async void OnOfflineDownload(object s, RoutedEventArgs e)
    {
        var tb = new TextBox { PlaceholderText = "输入下载链接 URL" };
        var d = new ContentDialog
        {
            Title = "离线下载",
            Content = tb,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        if (await d.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text))
            await VM.AddOfflineDownloadCommand.ExecuteAsync(tb.Text);
    }

    private async void OnBack(object s, RoutedEventArgs e) => await VM.GoBackAsync();

    private void OnDoubleTap(object s, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem f)
            VM.OpenFolderCommand.Execute(f);
    }

    private void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem f)
        {
            VM.SelectedFile = f;
            ShowFileMenu(e);
        }
    }

    private async void ShowFileMenu(RightTappedRoutedEventArgs e)
    {
        var f = VM.SelectedFile;
        if (f == null) return;

        var menu = new MenuFlyout();
        AddMenuItem(menu, "⬇ 下载", async () => await VM.DownloadFileCommand.ExecuteAsync(f));
        AddMenuItem(menu, "🔗 分享", async () => await VM.ShareFileCommand.ExecuteAsync(f));
        AddMenuItem(menu, "⭐ 收藏", async () => await VM.ToggleFavoriteCommand.ExecuteAsync(f));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "🗑 删除", async () => await VM.DeleteFileCommand.ExecuteAsync(f));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "ℹ 属性", async () => await VM.ShowPropsCommand.ExecuteAsync(f));

        menu.ShowAt(FileListView, e.GetPosition(FileListView));
    }

    private void AddMenuItem(MenuFlyout menu, string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (s, e) => await action();
        menu.Items.Add(item);
    }

    private async void OnDrop(object s, DragEventArgs e) => await VM.HandleDropAsync(e.DataView);

    private void OnDragOver(object s, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "拖拽上传到此目录";
    }
}
