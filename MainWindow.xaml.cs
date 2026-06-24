using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using StoraDesktop.ViewModels;
using StoraDesktop.Views;

namespace StoraDesktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        Title = "Stora Desktop";
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 720));

        // 显示登录页面
        ShowLoginPage();
    }

    public void ShowLoginPage()
    {
        var loginPage = App.Services.GetRequiredService<LoginPage>();
        loginPage.ViewModel.LoginSucceeded += OnLoginSucceeded;
        Content = loginPage;
    }

    private void OnLoginSucceeded(object? sender, System.EventArgs e)
    {
        // 切换到主页面
        var mainPage = App.Services.GetRequiredService<MainPage>();
        mainPage.ViewModel.LogoutSucceeded += OnLogoutSucceeded;
        Content = mainPage;
    }

    private void OnLogoutSucceeded(object? sender, System.EventArgs e)
    {
        ShowLoginPage();
    }
}