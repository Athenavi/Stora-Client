using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        var sidebar = new Grid();
        sidebar.Background = Application.Current.Resources["SystemControlBackgroundChromeMediumBrush"] as Brush
            ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
        var logo = new TextBlock
        {
            Text = "S",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(accentColor)
        };
        Grid.SetRow(logo, 0);

        var navStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(navStack, 1);

        _contentFrame = new Frame();
        Grid.SetColumn(_contentFrame, 1);

        var navItems = new[] {
            ("📁", "files"), ("🔗", "shares"), ("⭐", "favorites"),
            ("🖼", "photos"), ("🔐", "vault"), ("🏷", "tags"),
            ("☁", "sync")   // ← 新增同步盘
        };
        foreach (var (icon, tag) in navItems)
        {
            var b = new Button { Content = icon, FontSize = 20, Width = 40, Height = 40, Background = null, Tag = tag, Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(8) };
            b.Click += (s, e) => NavigateTo((string)((Button)s!).Tag);
            navStack.Children.Add(b);
        }

        sidebar.Children.Add(logo);
        sidebar.Children.Add(navStack);
        Grid.SetColumn(sidebar, 0);

        shell.Children.Add(sidebar);
        shell.Children.Add(_contentFrame);
        MainFrame.Content = shell;

        NavigateTo("files");
        _tray = new TrayService(this);
        _tray.ShowWindowRequested += () => DispatcherQueue.TryEnqueue(() => { try { this.AppWindow.Show(true); } catch { } });
        _tray.LogoutRequested += async () => { await App.Services.GetRequiredService<StoraApiClient>().LogoutAsync(); DispatcherQueue.TryEnqueue(() => { if (MainFrame != null) MainFrame.Content = CreateLoginPage(); }); };
        _tray.ExitRequested += () => DispatcherQueue.TryEnqueue(() => Application.Current.Exit());
        try { _tray.Initialize(); } catch { }
    }

    private Page CreateLoginPage()
    {
        var login = App.Services.GetRequiredService<LoginPage>();
        login.ViewModel.LoginSucceeded += (s, e) => ShowShell();
        return login;
    }

    private void NavigateTo(string page)
    {
        Page? instance = page switch
        {
            "files" => App.Services.GetRequiredService<FilePage>(),
            "shares" => App.Services.GetRequiredService<SharePage>(),
            "favorites" => App.Services.GetRequiredService<FavoritePage>(),
            "photos" => App.Services.GetRequiredService<PhotoPage>(),
            "vault" => App.Services.GetRequiredService<VaultPage>(),
            "tags" => App.Services.GetRequiredService<TagPage>(),
            "sync" => App.Services.GetRequiredService<SyncPage>(),
            _ => App.Services.GetRequiredService<FilePage>()
        };
        _contentFrame.Content = instance;
    }

    private Frame? _contentFrame;
    private TrayService? _tray;
}
