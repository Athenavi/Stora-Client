using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
            FileListView.SelectionChanged += OnSelectionChanged;
        };
    }

    private void AttachScroll()
    {
        var sv = FindChild<ScrollViewer>(FileListView);
        if (sv != null)
            sv.ViewChanged += (s, a) =>
            {
                var sc = (ScrollViewer)s;
                if (sc.VerticalOffset > sc.ScrollableHeight - 200 && VM.HasMore)
                    VM.LoadMoreCommand.Execute(null);
            };
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var c = VisualTreeHelper.GetChild(parent, i);
            if (c is T t) return t;
            var f = FindChild<T>(c); if (f != null) return f;
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
        foreach (var f in files) await VM.ChunkedUploadAsync(f);
        await VM.InitAsync();
    }

    private async void OnShare(object s, RoutedEventArgs e)
    {
        await VM.ShareFileCommand.ExecuteAsync(VM.SelectedFile);
    }

    private void OnCopy(object s, RoutedEventArgs e)
    {
        VM.CopyFilesCommand.Execute(null);
    }

    private void OnCut(object s, RoutedEventArgs e)
    {
        VM.CutFilesCommand.Execute(null);
    }

    private async void OnPaste(object s, RoutedEventArgs e)
    {
        await VM.PasteFilesCommand.ExecuteAsync(null);
    }

    private async void OnBack(object s, RoutedEventArgs e) => await VM.GoBackAsync();

    private async void OnOfflineDownload(object s, RoutedEventArgs e)
    {
        var tb = new TextBox { PlaceholderText = "Enter download URL" };
        var d = new ContentDialog
        {
            Title = "Offline Download",
            Content = tb,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        if (await d.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text))
            await VM.AddOfflineDownloadCommand.ExecuteAsync(tb.Text);
    }

    private void OnDoubleTap(object s, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem f)
            VM.OpenFolderCommand.Execute(f);
    }

    private void OnSelectionChanged(object s, SelectionChangedEventArgs e)
    {
        VM.SelectedFiles.Clear();
        foreach (var item in FileListView.SelectedItems)
            if (item is FileItem fi) VM.SelectedFiles.Add(fi);
        VM.SelectedCount = VM.SelectedFiles.Count;
    }

    private void OnClearSelection(object s, RoutedEventArgs e)
    {
        FileListView.SelectedItems.Clear();
        VM.ClearSelectionCommand.Execute(null);
    }

    private async void OnBatchDelete(object s, RoutedEventArgs e)
    {
        await VM.DeleteSelectedCommand.ExecuteAsync(null);
    }

    private void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem f)
        {
            VM.SelectedFile = f;
            if (!VM.SelectedFiles.Contains(f))
            {
                VM.SelectedFiles.Clear();
                VM.SelectedFiles.Add(f);
                VM.SelectedCount = 1;
            }
            ShowFileMenu(e);
        }
    }

    private async void ShowFileMenu(RightTappedRoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        if (VM.SelectedFiles.Count > 1)
        {
            AddItem(menu, "Share All", async () => await VM.ShareSelectedCommand.ExecuteAsync(null));
            AddItem(menu, "Copy All", () => VM.CopyFilesCommand.Execute(null));
            AddItem(menu, "Cut All", () => VM.CutFilesCommand.Execute(null));
            menu.Items.Add(new MenuFlyoutSeparator());
            AddItem(menu, "Delete All", async () => await VM.DeleteSelectedCommand.ExecuteAsync(null));
            menu.ShowAt(FileListView, e.GetPosition(FileListView));
            return;
        }

        var f = VM.SelectedFile;
        if (f == null) return;

        AddItem(menu, "Download", async () => await VM.DownloadFileCommand.ExecuteAsync(f));
        AddItem(menu, "Share", async () => await VM.ShareFileCommand.ExecuteAsync(f));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu, "Copy", () => VM.CopyFilesCommand.Execute(null));
        AddItem(menu, "Cut", () => VM.CutFilesCommand.Execute(null));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu, "Preview", () => PreviewFile(f));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu, "Favorite", async () => await VM.ToggleFavoriteCommand.ExecuteAsync(f));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu, "Delete", async () => await VM.DeleteFileCommand.ExecuteAsync(f));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddItem(menu, "Properties", async () => await VM.ShowPropsCommand.ExecuteAsync(f));

        menu.ShowAt(FileListView, e.GetPosition(FileListView));
    }

    private static void PreviewFile(FileItem? f)
    {
        if (f == null) return;
        var baseUrl = App.Services.GetRequiredService<Services.StoraApiClient>().BaseUrl;
        var url = $"{baseUrl}/api/v2/files/preview/{f.Id}/{Uri.EscapeDataString(f.Name ?? "")}";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void AddItem(MenuFlyout menu, string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (s, e) => await action(); menu.Items.Add(item);
    }

    private void AddItem(MenuFlyout menu, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (s, e) => action(); menu.Items.Add(item);
    }

    private async void OnDrop(object s, DragEventArgs e) => await VM.HandleDropAsync(e.DataView);

    private void OnDragOver(object s, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Upload here";
    }
}
