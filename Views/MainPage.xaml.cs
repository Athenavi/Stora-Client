using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;

namespace StoraDesktop.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }
    private ScrollViewer? _listScrollViewer;

    public MainPage(MainViewModel viewModel)
    {
        this.InitializeComponent();
        ViewModel = viewModel;
        this.Loaded += async (s, e) =>
        {
            await ViewModel.InitializeCommand.ExecuteAsync(null);
            AttachScrollDetection();
        };
    }

    private void AttachScrollDetection()
    {
        _listScrollViewer = FindScrollViewer(FileListView);
        if (_listScrollViewer != null)
            _listScrollViewer.ViewChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender!;
        if (sv.VerticalOffset > sv.ScrollableHeight - 200 && ViewModel.HasMoreFiles)
        {
            ViewModel.LoadMoreFilesCommand.Execute(null);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private void OnFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem file && file.IsFolder)
        {
            ViewModel.OpenFolderCommand.Execute(file);
        }
    }

    private void OnFileRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem file)
        {
            ViewModel.SelectedFile = file;
        }
    }
}