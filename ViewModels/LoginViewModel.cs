using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoraDesktop.Services;
using System;
using System.Threading.Tasks;

namespace StoraDesktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly StoraApiClient _apiClient;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _serverUrl = "http://localhost:9421";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // 登录成功事件（让 MainWindow 监听并跳转）
    public event EventHandler? LoginSucceeded;

    public LoginViewModel(StoraApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "请输入用户名和密码";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            _apiClient.BaseUrl = ServerUrl.TrimEnd('/');
            await _apiClient.LoginAsync(Username, Password);

            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"登录失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}