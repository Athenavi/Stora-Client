using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StoraDesktop.Views;

public sealed partial class SyncPage : Page
{
    public SyncViewModel VM { get; }

    public SyncPage(SyncViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
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
                        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 34, 187, 102));
                        container.Background = green;
                        _ = Task.Delay(400).ContinueWith(_ =>
                        {
                            DispatcherQueue.TryEnqueue(() => container.Background = null);
                        });
                    }
                    break;
                }
            }
        });
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
