using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace StoraDesktop.ViewModels;

public partial class ShareViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "就绪";
    public ObservableCollection<ShareItem> Shares { get; } = new();

    public ShareViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true; _status = "加载中...";
            var items = await _api.GetSharesAsync();
            Shares.Clear();
            foreach (var s in items) Shares.Add(s);
            _status = $"共 {items.Count} 个分享";
        }
        catch (Exception ex)
        {
            _status = $"加载失败: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task DeleteShareAsync(ShareItem? s)
    {
        if (s == null) return;
        try
        {
            await _api.DeleteShareAsync(s.Id.ToString());
            Shares.Remove(s);
            _status = "分享已删除";
        }
        catch (Exception ex) { _status = $"删除失败: {ex.Message}"; }
    }

    [RelayCommand]
    public void CopyLink(ShareItem? s)
    {
        if (s == null) return;
        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pkg.SetText(s.ShareUrl);
        Clipboard.SetContent(pkg);
        _status = "链接已复制";
    }
}
