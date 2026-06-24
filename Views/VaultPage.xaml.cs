using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;
using System;

namespace StoraDesktop.Views;

public sealed partial class VaultPage : Page
{
    public VaultViewModel VM { get; }

    public VaultPage(VaultViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) => await VM.LoadAsync();
    }

    private async void OnCreate(object s, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "保险箱名称" };
        var pwdBox = new PasswordBox { PlaceholderText = "密码" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(pwdBox);

        var d = new ContentDialog
        {
            Title = "新建保险箱",
            Content = panel,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };

        if (await d.ShowAsync() == ContentDialogResult.Primary)
        {
            VM.NewVaultName = nameBox.Text;
            VM.NewVaultPwd = pwdBox.Password;
            await VM.CreateVaultAsync();
        }
    }

    private async void OnUnlock(object s, DoubleTappedRoutedEventArgs e)
    {
        var vault = (e.OriginalSource as FrameworkElement)?.DataContext as VaultItem;
        if (vault == null) return;

        var pwdBox = new PasswordBox { PlaceholderText = $"输入 \"{vault.Name}\" 的密码" };
        var d = new ContentDialog
        {
            Title = "解锁保险箱",
            Content = pwdBox,
            PrimaryButtonText = "解锁",
            CloseButtonText = "取消",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };

        if (await d.ShowAsync() == ContentDialogResult.Primary)
        {
            VM.UnlockingVaultId = vault.Id;
            VM.UnlockingVaultName = vault.Name;
            VM.UnlockPwd = pwdBox.Password;
            await VM.UnlockVaultAsync();
        }
    }
}
