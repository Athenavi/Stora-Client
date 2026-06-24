using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace StoraDesktop.ViewModels;

public partial class FavoriteViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "就绪";
    public ObservableCollection<FileItem> Files { get; } = new();

    public FavoriteViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true; _status = "加载中...";
            var r = await _api.GetFilesAsync(isFavorite: true, perPage: 200);
            Files.Clear();
            foreach (var f in r.Items) Files.Add(f);
            _status = $"共 {r.Total} 个收藏";
        }
        catch (Exception ex) { _status = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RemoveFavoriteAsync(FileItem? f)
    {
        if (f == null) return;
        try
        {
            await _api.ToggleFavoriteAsync(f.Id.ToString());
            Files.Remove(f);
            _status = "已取消收藏";
        }
        catch (Exception ex) { _status = $"操作失败: {ex.Message}"; }
    }
}
