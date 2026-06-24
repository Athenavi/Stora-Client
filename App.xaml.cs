using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using StoraDesktop.Services;
using StoraDesktop.ViewModels;
using StoraDesktop.Views;
using System;

namespace StoraDesktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow MainAppWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        var sc = new ServiceCollection();
        sc.AddSingleton<StoraApiClient>();
        sc.AddTransient<LoginViewModel>();
        sc.AddTransient<LoginPage>();
        sc.AddTransient<FileViewModel>();
        sc.AddTransient<FilePage>();
        sc.AddTransient<ShareViewModel>();
        sc.AddTransient<SharePage>();
        sc.AddTransient<FavoriteViewModel>();
        sc.AddTransient<FavoritePage>();
        sc.AddTransient<PhotoViewModel>();
        sc.AddTransient<PhotoPage>();
        sc.AddTransient<VaultViewModel>();
        sc.AddTransient<VaultPage>();
        sc.AddTransient<TagViewModel>();
        sc.AddTransient<TagPage>();
        Services = sc.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }
}
