using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.ViewModels;

namespace StoraDesktop.Views;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; }

    public LoginPage(LoginViewModel viewModel)
    {
        this.InitializeComponent();
        ViewModel = viewModel;
    }
}