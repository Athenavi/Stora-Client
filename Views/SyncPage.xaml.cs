using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StoraDesktop.Models;
using StoraDesktop.Services;
using StoraDesktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StoraDesktop.Views;

public sealed partial class SyncPage : Page
{
    public SyncViewModel VM { get; }
    private readonly SyncService _syncService;

    public SyncPage(SyncViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        _syncService = (App.Services.GetService(typeof(SyncService)) as SyncService)!;
        VM.FileFlashRequested += OnFileFlash;
    }

    private async void OnFileFlash(string fileName)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var item in SyncList.Items)
            {
                if (item is SyncFileState sfs && sfs.FileName == fileName)
                {
                    if (SyncList.ContainerFromItem(item) is ListViewItem container)
                    {
                        container.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 34, 187, 102));
                        _ = Task.Delay(400).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => container.Background = null));
                    }
                    break;
                }
            }
        });
    }

    private async void OnFileRightTap(object sender, RightTappedRoutedEventArgs e)
    {
        var file = (e.OriginalSource as FrameworkElement)?.DataContext as SyncFileState;
        if (file == null) return;

        var menu = new MenuFlyout();
        var removeItem = new MenuFlyoutItem { Text = "Remove ghost from list" };
        removeItem.Click += (s, args) => _syncService.ClearGhostFiles();
        menu.Items.Add(removeItem);

        if (!string.IsNullOrEmpty(file.LocalPath))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var locItem = new MenuFlyoutItem { Text = "Show in folder" };
            locItem.Click += (s, args) => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{System.IO.Path.Combine(_syncService.Store.Config.LocalPath, file.LocalPath)}\"");
            menu.Items.Add(locItem);
        }

        menu.ShowAt(SyncList, e.GetPosition(SyncList));
    }

    private async void OnFileDoubleTap(object sender, DoubleTappedRoutedEventArgs e)
    {
        var file = (e.OriginalSource as FrameworkElement)?.DataContext as SyncFileState;
        if (file == null || file.Versions.Count == 0) return;

        var items = new List<string>();
        foreach (var v in file.Versions)
            items.Add($"v{v.Version} | {v.CreatedAt:yyyy-MM-dd HH:mm:ss} | {v.SizeDisplay} | {v.Reason}");

        var d = new ContentDialog
        {
            Title = $"Version history: {file.FileName} (current v{file.CurrentVersion})",
            Content = new ListView { ItemsSource = items, Height = 200 },
            CloseButtonText = "Close",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        await d.ShowAsync();
    }
}
