using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    }

    private async void OnFileDoubleTap(object sender, DoubleTappedRoutedEventArgs e)
    {
        var file = (e.OriginalSource as FrameworkElement)?.DataContext as SyncFileState;
        if (file == null || file.Versions.Count == 0) return;

        var items = new List<string>();
        foreach (var v in file.Versions)
            items.Add($"v{v.Version} | {v.CreatedAt:yyyy-MM-dd HH:mm:ss} | {v.SizeDisplay} | {v.Reason}");

        var listBox = new ListView { ItemsSource = items, Height = 200 };

        var d = new ContentDialog
        {
            Title = $"版本历史: {file.FileName} (当前 v{file.CurrentVersion})",
            Content = listBox,
            CloseButtonText = "关闭",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        await d.ShowAsync();
    }
}
