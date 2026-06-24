using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;

namespace StoraDesktop.Views;

public sealed partial class SyncPage : Page
{
    public SyncViewModel VM { get; }

    public SyncPage(SyncViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
    }
}
