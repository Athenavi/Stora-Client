using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace StoraDesktop.ViewModels;

public partial class PhotoViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private FileItem? _selectedPhoto;
    [ObservableProperty] private bool _showPreview;

    public ObservableCollection<FileItem> Photos { get; } = new();

    public PhotoViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true; _status = "加载中...";
            var r = await _api.GetFilesAsync(fileType: "image", perPage: 200);
            Photos.Clear();
            foreach (var f in r.Items) Photos.Add(f);
            _status = $"共 {r.Total} 张照片";
        }
        catch (Exception ex) { _status = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void OpenPreview(FileItem? f)
    {
        if (f == null) return;
        SelectedPhoto = f;
        ShowPreview = true;
    }

    [RelayCommand]
    public void ClosePreview() => ShowPreview = false;
}
