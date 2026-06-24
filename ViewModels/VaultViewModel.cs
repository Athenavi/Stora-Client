using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace StoraDesktop.ViewModels;

public partial class VaultViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private bool _showCreateDialog;
    [ObservableProperty] private string _newVaultName = "";
    [ObservableProperty] private string _newVaultPwd = "";
    [ObservableProperty] private bool _showUnlock;
    [ObservableProperty] private string _unlockPwd = "";
    [ObservableProperty] private long _unlockingVaultId;
    [ObservableProperty] private string _unlockingVaultName = "";

    public ObservableCollection<VaultItem> Vaults { get; } = new();
    public ObservableCollection<VaultFileItem> VaultFiles { get; } = new();
    private string? _currentVaultToken;

    public VaultViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true; _status = "加载中...";
            var items = await _api.GetVaultsAsync();
            Vaults.Clear();
            foreach (var v in items) Vaults.Add(v);
            _status = $"共 {items.Count} 个保险箱";
        }
        catch (Exception ex) { _status = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(_newVaultName) || string.IsNullOrWhiteSpace(_newVaultPwd)) return;
        try
        {
            await _api.CreateVaultAsync(_newVaultName, _newVaultPwd);
            _showCreateDialog = false;
            _newVaultName = _newVaultPwd = "";
            await LoadAsync();
        }
        catch (Exception ex) { _status = $"创建失败: {ex.Message}"; }
    }

    [RelayCommand]
    public void ShowUnlockDialog(VaultItem? v)
    {
        if (v == null) return;
        _unlockingVaultId = v.Id;
        _unlockingVaultName = v.Name;
        _unlockPwd = "";
        _showUnlock = true;
    }

    [RelayCommand]
    public async Task UnlockVaultAsync()
    {
        try
        {
            _currentVaultToken = await _api.VerifyVaultPasswordAsync(_unlockingVaultId, _unlockPwd);
            _showUnlock = false;
            var items = await _api.GetVaultItemsAsync(_unlockingVaultId, _currentVaultToken);
            VaultFiles.Clear();
            foreach (var f in items) VaultFiles.Add(f);
            _status = $"保险箱 {_unlockingVaultName}: {items.Count} 个文件";
        }
        catch (Exception ex) { _status = $"解锁失败: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task DeleteVaultAsync(VaultItem? v)
    {
        if (v == null) return;
        try
        {
            await _api.DeleteVaultAsync(v.Id);
            Vaults.Remove(v);
            _status = "保险箱已删除";
        }
        catch (Exception ex) { _status = $"删除失败: {ex.Message}"; }
    }

    [RelayCommand]
    public void BackToVaults()
    {
        VaultFiles.Clear();
        _currentVaultToken = null;
        _status = "就绪";
    }
}
