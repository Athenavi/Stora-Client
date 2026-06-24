using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using StoraDesktop;
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

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<StoraApiClient>();
        serviceCollection.AddTransient<LoginViewModel>();
        serviceCollection.AddTransient<LoginPage>();
        serviceCollection.AddTransient<MainViewModel>();   // 新增
        serviceCollection.AddTransient<MainPage>();         // 新增
        Services = serviceCollection.BuildServiceProvider();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }
}