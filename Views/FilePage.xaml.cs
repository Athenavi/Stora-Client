using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
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

    private async void PreviewFile(FileItem? f)
    {
        if (f == null) return;
        var baseUrl = App.Services.GetRequiredService<Services.StoraApiClient>().BaseUrl;

        var ext = (System.IO.Path.GetExtension(f.Name ?? "") ?? "").ToLower();
        var downloadUrl = $"{baseUrl}/api/v2/files/{f.Id}/download";
        var encodedUrl = Uri.EscapeDataString(downloadUrl);
        PreviewTitle.Text = $"Preview: {f.Name}";

        PreviewWebView.Visibility = Visibility.Collapsed;
        PreviewMediaPlayer.Visibility = Visibility.Collapsed;
        PreviewMediaPlayer.Source = null;

        var videoExts = new[] { ".mp4", ".webm", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".m4v" };
        var audioExts = new[] { ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a" };
        var imageExts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico" };
        var officeExts = new[] { ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt" };
        var textExts = new[] { ".txt", ".csv", ".md", ".json", ".xml", ".log" };

        if (Array.IndexOf(videoExts, ext) >= 0 || Array.IndexOf(audioExts, ext) >= 0)
        {
            // MediaPlayerElement can not send auth headers, download to temp file
            _ = Task.Run(async () =>
            {
                try
                {
                    var api = App.Services.GetRequiredService<Services.StoraApiClient>();
                    var stream = await api.DownloadFileAsync(f.Id.ToString());
                    var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Stora_" + f.Name);
                    using (var fs = new System.IO.FileStream(tempFile, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                    {
                        await stream.CopyToAsync(fs);
                    }
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        PreviewMediaPlayer.Source = MediaSource.CreateFromUri(new Uri(tempFile));
                        PreviewMediaPlayer.Visibility = Visibility.Visible;
                        PreviewOverlay.Visibility = Visibility.Visible;
                        PreviewTitle.Text = $"Playing: {f.Name}";
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() => PreviewTitle.Text = $"Playback failed: {ex.Message}");
                }
            });
            return;
        }
        else if (Array.IndexOf(imageExts, ext) >= 0)
        {
            PreviewWebView.Source = new Uri(downloadUrl);
            PreviewWebView.Visibility = Visibility.Visible;
        }
        else if (Array.IndexOf(officeExts, ext) >= 0)
        {
            PreviewWebView.Source = new Uri($"https://view.officeapps.live.com/op/view.aspx?src={encodedUrl}");
            PreviewWebView.Visibility = Visibility.Visible;
        }
        else if (ext == ".pdf")
        {
            PreviewWebView.Source = new Uri($"https://docs.google.com/viewer?url={encodedUrl}&embedded=true");
            PreviewWebView.Visibility = Visibility.Visible;
        }
        else if (Array.IndexOf(textExts, ext) >= 0)
        {
            PreviewWebView.Source = new Uri(downloadUrl);
            PreviewWebView.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewTitle.Text = $"Preview not available for {f.Name}";
            return;
        }
        PreviewOverlay.Visibility = Visibility.Visible;
    }

    private void OnClosePreview(object s, RoutedEventArgs e)
    {
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewWebView.Source = null;
    }

    private void OnOpenInBrowser(object s, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PreviewWebView.Source?.ToString() ?? "") { UseShellExecute = true }); }
        catch { }
    }

    private void OnPreviewBgClick(object s, PointerRoutedEventArgs e) => OnClosePreview(s, new RoutedEventArgs());

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
