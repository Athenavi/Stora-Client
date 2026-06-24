using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using System.IO;

namespace StoraDesktop.ViewModels;

public partial class VaultViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    private string? _currentVaultToken;
    private VaultItem? _currentVault;

    // View state: "list" / "unlock" / "files"
    [ObservableProperty] private string _viewState = "list";
    public Visibility IsListView => _viewState == "list" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsUnlockView => _viewState == "unlock" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsFilesView => _viewState == "files" ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _vaultName = "";
    [ObservableProperty] private string _vaultPwd = "";
    [ObservableProperty] private string _vaultConfirm = "";

    public ObservableCollection<VaultItem> Vaults { get; } = new();
    public ObservableCollection<VaultFileItem> VaultFiles { get; } = new();

    public VaultViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try { IsLoading = true; _status = "Loading...";
            var items = await _api.GetVaultsAsync();
            Vaults.Clear();
            foreach (var v in items) Vaults.Add(v);
            _status = $"{items.Count} vault(s)";
        } catch (Exception ex) { _status = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultName) || string.IsNullOrWhiteSpace(VaultPwd))
        { _status = "Name and password required"; return; }
        if (VaultPwd != VaultConfirm)
        { _status = "Passwords do not match"; return; }
        if (VaultPwd.Length < 6)
        { _status = "Password must be at least 6 characters"; return; }
        try
        {
            await _api.CreateVaultAsync(VaultName, VaultPwd);
            VaultName = VaultPwd = VaultConfirm = "";
            _status = "Vault created";
            await LoadAsync();
        }
        catch (Exception ex) { _status = $"Create failed: {ex.Message}"; }
    }

    [RelayCommand]
    public void ShowUnlock(VaultItem? v)
    {
        if (v == null) return;
        _currentVault = v;
        VaultName = v.Name;
        VaultPwd = "";
        _viewState = "unlock"; OnPropertyChanged(nameof(ViewState)); OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(IsUnlockView)); OnPropertyChanged(nameof(IsFilesView));
    }

    [RelayCommand]
    public async Task UnlockAsync()
    {
        if (_currentVault == null || string.IsNullOrWhiteSpace(VaultPwd)) return;
        try
        {
            IsLoading = true;
            _currentVaultToken = await _api.VerifyVaultPasswordAsync(_currentVault.Id, VaultPwd);
            _viewState = "files"; OnPropertyChanged(nameof(ViewState)); OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(IsUnlockView)); OnPropertyChanged(nameof(IsFilesView));
            await LoadFilesAsync();
        }
        catch (Exception ex) { _status = $"Unlock failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadFilesAsync()
    {
        if (_currentVault == null || _currentVaultToken == null) return;
        try { IsLoading = true; _status = "Loading files...";
            var items = await _api.GetVaultItemsAsync(_currentVault.Id, _currentVaultToken);
            VaultFiles.Clear();
            foreach (var f in items) VaultFiles.Add(f);
            _status = $"{items.Count} file(s)";
        } catch (Exception ex) { _status = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void Lock()
    {
        _currentVaultToken = null;
        _currentVault = null;
        VaultFiles.Clear();
        _viewState = "list"; OnPropertyChanged(nameof(ViewState)); OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(IsUnlockView)); OnPropertyChanged(nameof(IsFilesView));
        _status = "Locked";
    }

    [RelayCommand]
    public void BackToList()
    {
        _currentVaultToken = null;
        _currentVault = null;
        VaultFiles.Clear();
        _viewState = "list"; OnPropertyChanged(nameof(ViewState)); OnPropertyChanged(nameof(IsListView)); OnPropertyChanged(nameof(IsUnlockView)); OnPropertyChanged(nameof(IsFilesView));
    }

    [RelayCommand]
    public async Task DeleteVaultAsync(VaultItem? v)
    {
        if (v == null) return;
        var confirm = new ContentDialog
        {
            Title = "Delete vault?", Content = $"Permanently delete \"{v.Name}\"? All files will be lost.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try { await _api.DeleteVaultAsync(v.Id); Vaults.Remove(v); _status = "Deleted"; }
        catch (Exception ex) { _status = $"Delete failed: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task DeleteFileAsync(VaultFileItem? f)
    {
        if (f == null || _currentVault == null || _currentVaultToken == null) return;
        try { await _api.DeleteVaultItemAsync(_currentVault.Id, f.Id, _currentVaultToken);
            VaultFiles.Remove(f); _status = "File deleted"; }
        catch (Exception ex) { _status = $"Delete failed: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task DownloadFileAsync(VaultFileItem? f)
    {
        if (f == null || _currentVault == null || _currentVaultToken == null) return;
        try
        {
            _status = $"Downloading {f.Name}...";
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedFileName = f.Name ?? "file";
            picker.FileTypeChoices.Add("All files", new System.Collections.Generic.List<string> { "." });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var stream = await _api.DownloadVaultItemAsync(_currentVault.Id, f.Id, _currentVaultToken);
            using var fs = await file.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fs);
            _status = "Downloaded";
        }
        catch (Exception ex) { _status = $"Download failed: {ex.Message}"; }
    }
}
