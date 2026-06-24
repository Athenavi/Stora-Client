using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;

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

    private void OnPhotoTap(object s, PointerRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem f)
            VM.OpenPreviewCommand.Execute(f);
    }

    private void ClosePreview(object s, PointerRoutedEventArgs e) => VM.ClosePreviewCommand.Execute(null);
}
