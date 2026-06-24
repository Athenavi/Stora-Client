using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using System.IO;

namespace StoraDesktop.ViewModels;

public partial class PhotoViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    private int _page = 1, _total;

    [ObservableProperty] private bool _isLoading, _loadingMore;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private FileItem? _selectedPhoto;
    [ObservableProperty] private bool _showPreview;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _hasSelection;

    public bool HasMore => _page * 200 < _total;
    public ObservableCollection<FileItem> Photos { get; } = new();
    public ObservableCollection<FileItem> SelectedPhotos { get; } = new();

    public PhotoViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _page = 1;
        try { IsLoading = true; _status = "Loading...";
            var r = await _api.GetFilesAsync(fileType: "image", page: _page, perPage: 200);
            _total = r.Total; Photos.Clear();
            foreach (var f in r.Items) Photos.Add(f);
            _status = $"{r.Total} photos";
        } finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (!HasMore || _loadingMore) return;
        _page++;
        try { _loadingMore = true;
            var r = await _api.GetFilesAsync(fileType: "image", page: _page, perPage: 200);
            foreach (var f in r.Items) Photos.Add(f);
            _status = HasMore ? $"Loaded {Photos.Count}/{_total}" : $"{_total} photos";
        } finally { _loadingMore = false; }
    }

    [RelayCommand]
    public void OpenPreview(FileItem? f)
    {
        if (f == null) return;
        SelectedPhoto = f; ShowPreview = true;
    }

    [RelayCommand]
    public void ClosePreview() { ShowPreview = false; SelectedPhoto = null; }

    [RelayCommand]
    public void NextPhoto()
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        var idx = Photos.IndexOf(SelectedPhoto);
        if (idx < Photos.Count - 1) SelectedPhoto = Photos[idx + 1];
    }

    [RelayCommand]
    public void PrevPhoto()
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        var idx = Photos.IndexOf(SelectedPhoto);
        if (idx > 0) SelectedPhoto = Photos[idx - 1];
    }

    public void ToggleSelection(FileItem f)
    {
        if (SelectedPhotos.Contains(f)) { SelectedPhotos.Remove(f); }
        else { SelectedPhotos.Add(f); }
        SelectedCount = SelectedPhotos.Count;
        HasSelection = SelectedCount > 0;
    }

    [RelayCommand]
    public void ClearSelection() { SelectedPhotos.Clear(); SelectedCount = 0; HasSelection = false; }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (SelectedPhotos.Count == 0) return;
        var confirm = new ContentDialog
        {
            Title = "Delete photos?", Content = $"Delete {SelectedPhotos.Count} photo(s)?",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var f in SelectedPhotos.ToList())
        {
            try { await _api.DeleteFileAsync(f.Id.ToString()); Photos.Remove(f); } catch { }
        }
        ClearSelection(); _status = "Deleted";
    }

    [RelayCommand]
    public async Task DownloadSelectedAsync()
    {
        if (SelectedPhotos.Count == 0) return;
        var ids = SelectedPhotos.Select(f => f.Id).ToList();
        // Single file download if only one
        if (ids.Count == 1)
        {
            var f = SelectedPhotos[0];
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedFileName = f.Name ?? "photo";
            picker.FileTypeChoices.Add("All files", new System.Collections.Generic.List<string> { "." });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var stream = await _api.DownloadFileAsync(f.Id.ToString());
            using var fs = await file.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fs);
            _status = "Downloaded";
            return;
        }
        _status = "Batch download not available yet";
    }
}
