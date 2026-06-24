using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;

namespace StoraDesktop.Views;

public sealed partial class PhotoPage : Page
{
    public PhotoViewModel VM { get; }

    public PhotoPage(PhotoViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) => await VM.LoadAsync();
    }

    private async void OnRefresh(object s, RoutedEventArgs e) => await VM.LoadAsync();

    private void OnPhotoClick(object s, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FileItem f)
            VM.OpenPreviewCommand.Execute(f);
    }

    private void OnPhotoPointerPressed(object s, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            var f = (e.OriginalSource as FrameworkElement)?.DataContext as FileItem;
            if (f != null) VM.ToggleSelection(f);
        }
    }

    private void OnPreviewBgClick(object s, PointerRoutedEventArgs e) => VM.ClosePreviewCommand.Execute(null);

    private void OnNextPhoto(object s, RoutedEventArgs e) => VM.NextPhotoCommand.Execute(null);
    private void OnPrevPhoto(object s, RoutedEventArgs e) => VM.PrevPhotoCommand.Execute(null);

    private async void OnDeleteSelected(object s, RoutedEventArgs e) => await VM.DeleteSelectedCommand.ExecuteAsync(null);
    private async void OnDownloadSelected(object s, RoutedEventArgs e) => await VM.DownloadSelectedCommand.ExecuteAsync(null);
    private void OnClearSelection(object s, RoutedEventArgs e) => VM.ClearSelectionCommand.Execute(null);

    private async void OnScrollView(object s, ScrollViewerViewChangedEventArgs e)
    {
        var sv = (ScrollViewer)s;
        if (sv.VerticalOffset > sv.ScrollableHeight - 300 && VM.HasMore)
            await VM.LoadMoreCommand.ExecuteAsync(null);
    }
}
