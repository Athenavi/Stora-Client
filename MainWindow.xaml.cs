using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Services;
using StoraDesktop.ViewModels;
using StoraDesktop.Views;
using System;

namespace StoraDesktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        Title = "Stora Desktop";
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));

        var login = App.Services.GetRequiredService<LoginPage>();
        login.ViewModel.LoginSucceeded += (s, e) => ShowShell();
        MainFrame.Content = login;
    }

    private void ShowShell()
    {
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sidebar
        var sidebar = new Grid();
        sidebar.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlBackgroundChromeMediumBrush"];
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = new TextBlock { Text = "S", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        logo.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemAccentColor"];
        Grid.SetRow(logo, 0);

        var navStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(navStack, 1);

        var navItems = new[] { ("📁", "files"), ("🔗", "shares"), ("⭐", "favorites"), ("🖼", "photos"), ("🔐", "vault"), ("🏷", "tags") };
        foreach (var (icon, tag) in navItems)
        {
            var b = new Button { Content = icon, FontSize = 20, Width = 40, Height = 40, Background = null, Tag = tag, Margin = new Thickness(0,0,0,4), CornerRadius = new CornerRadius(8) };
            b.Click += (s, e) => NavigateTo((string)((Button)s!).Tag);
            navStack.Children.Add(b);
        }

        var logoutBtn = new Button { Content = "🚪", FontSize = 18, Width = 40, Height = 40, Background = null, Margin = new Thickness(0,0,0,8), CornerRadius = new CornerRadius(8) };
        logoutBtn.Click += async (s, e) => { await App.Services.GetRequiredService<StoraApiClient>().LogoutAsync(); MainFrame.Content = CreateLoginPage(); };
        Grid.SetRow(logoutBtn, 2);

        sidebar.Children.Add(logo);
        sidebar.Children.Add(navStack);
        sidebar.Children.Add(logoutBtn);
        Grid.SetColumn(sidebar, 0);

        var contentFrame = new Frame();
        Grid.SetColumn(contentFrame, 1);
        contentFrame.Navigate(typeof(FilePage), null);
        _frame = contentFrame;

        shell.Children.Add(sidebar);
        shell.Children.Add(contentFrame);
        MainFrame.Content = shell;
    }

    private Page CreateLoginPage()
    {
        var login = App.Services.GetRequiredService<LoginPage>();
        login.ViewModel.LoginSucceeded += (s, e) => ShowShell();
        return login;
    }

    private void NavigateTo(string page)
    {
        if (_frame == null) return;
        _frame.Navigate(page switch
        {
            "files" => typeof(FilePage),
            "shares" => typeof(SharePage),
            "favorites" => typeof(FavoritePage),
            "photos" => typeof(PhotoPage),
            "vault" => typeof(VaultPage),
            "tags" => typeof(TagPage),
            _ => typeof(FilePage)
        }, null);
    }

    private Frame? _frame;
}
