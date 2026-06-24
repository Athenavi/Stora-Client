using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StoraDesktop.ViewModels;

public partial class FileViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    private int _page = 1, _total;
    private const int PageSize = 50;
    private UserInfo? _user;

    [ObservableProperty] private string _currPath = "/";
    [ObservableProperty] private string? _currFolderId;
    [ObservableProperty] private bool _isLoading, _loadingMore;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private FileItem? _selectedFile;
    [ObservableProperty] private bool _showUploadPanel;
    [ObservableProperty] private double _uploadProgress;
    [ObservableProperty] private string _uploadStatus = "";

    public bool HasMore => _page * PageSize < _total;
    public ObservableCollection<FileItem> Files { get; } = new();
    public string Username => _user?.Username ?? "";
    public event EventHandler? NavigateToShare;

    public FileViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand] public async Task InitAsync()
    {
        _user = await _api.GetCurrentUserAsync();
        OnPropertyChanged(nameof(Username));
        _page = 1; await FetchAsync(true);
    }

    private async Task FetchAsync(bool clear)
    {
        try
        {
            if (clear) IsLoading = true; else _loadingMore = true;
            var r = await _api.GetFilesAsync(_currFolderId, _page, PageSize);
            _total = r.Total;
            if (clear) Files.Clear();
            foreach (var f in r.Items) Files.Add(f);
            _status = HasMore ? $"已加载 {Files.Count}/{_total}" : $"共 {_total} 个";
        }
        finally { IsLoading = false; _loadingMore = false; }
    }

    [RelayCommand] public async Task LoadMoreAsync()
    {
        if (!HasMore || _loadingMore) return;
        _page++; await FetchAsync(false);
    }

    [RelayCommand] public async Task OpenFolderAsync(FileItem? f)
    {
        if (f == null || !f.IsFolder) return;
        _currFolderId = f.Id.ToString();
        _currPath += $"/{f.Name}";
        _page = 1; await FetchAsync(true);
    }

    [RelayCommand] public async Task GoBackAsync()
    {
        _currFolderId = null; _currPath = "/"; _page = 1;
        await FetchAsync(true);
    }

    [RelayCommand] public async Task DownloadFileAsync(FileItem? f)
    {
        if (f == null) return;
        try
        {
            _status = $"下载 {f.Name}...";
            var picker = new FileSavePicker();
            picker.SuggestedFileName = f.Name ?? "download";
            picker.FileTypeChoices.Add("所有文件", new List<string> { "." });
            var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var stream = await _api.DownloadFileAsync(f.Id.ToString());
            using var fs = await file.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fs);
            _status = $"{f.Name} 下载完成";
        }
        catch (Exception ex) { _status = $"下载失败: {ex.Message}"; }
    }

    [RelayCommand] public async Task DeleteFileAsync(FileItem? f)
    {
        if (f == null) return;
        await _api.DeleteFileAsync(f.Id.ToString());
        Files.Remove(f);
        _status = $"{f.Name} 已删除";
    }

    [RelayCommand] public async Task ShareFileAsync(FileItem? f)
    {
        if (f == null) return;
        await _api.CreateShareAsync(f.Id);
        _status = "已创建分享链接";
        NavigateToShare?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] public async Task ToggleFavoriteAsync(FileItem? f)
    {
        if (f == null) return;
        await _api.ToggleFavoriteAsync(f.Id.ToString());
        f.IsFavorite = !f.IsFavorite;
    }

    [RelayCommand] public async Task ShowPropsAsync(FileItem? f)
    {
        if (f == null) return;
        var d = new ContentDialog
        {
            Title = "属性", CloseButtonText = "确定",
            Content = $"文件名: {f.Name}\n大小: {f.SizeDisplay}\n类型: {(f.IsFolder ? "文件夹" : f.MimeType ?? "未知")}\n创建: {f.CreatedAt}",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        await d.ShowAsync();
    }

    [RelayCommand] public async Task AddOfflineDownloadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        await _api.CreateOfflineDownloadAsync(url);
        _status = "已添加离线下载任务";
    }

    public async Task HandleDropAsync(Windows.ApplicationModel.DataTransfer.DataPackageView data)
    {
        if (!data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        var items = await data.GetStorageItemsAsync();
        _showUploadPanel = true;
        int ok = 0;
        foreach (var item in items)
        {
            if (item is StorageFile file)
            {
                _uploadStatus = $"上传 {file.Name}...";
                using var s = await file.OpenStreamForReadAsync();
                await _api.UploadFileAsync(s, file.Name, _currFolderId);
                ok++;
            }
        }
        _uploadStatus = $"完成 ({ok} 个)";
        _page = 1; await FetchAsync(true);
        await Task.Delay(2000);
        _showUploadPanel = false;
    }

    public async Task ChunkedUploadAsync(StorageFile file)
    {
        _showUploadPanel = true;
        try
        {
            var props = await file.GetBasicPropertiesAsync();
            var size = (long)props.Size;
            if (size < 10 * 1024 * 1024)
            {
                _uploadStatus = $"上传 {file.Name}...";
                using var s = await file.OpenStreamForReadAsync();
                await _api.UploadFileAsync(s, file.Name, _currFolderId);
                _uploadStatus = "完成";
                return;
            }
            _uploadStatus = "初始化分片...";
            var uploadId = await _api.InitChunkUploadAsync(file.Name, size, _currFolderId);
            const int cs = 5 * 1024 * 1024;
            using var stream = await file.OpenStreamForReadAsync();
            var buf = new byte[cs];
            int idx = 0, total = (int)Math.Ceiling((double)size / cs);
            while (true)
            {
                int read = await stream.ReadAsync(buf);
                if (read == 0) break;
                if (read < cs) Array.Resize(ref buf, read);
                _uploadStatus = $"分片 {idx + 1}/{total} ({100 * idx / total}%)";
                _uploadProgress = (double)idx / total;
                await _api.UploadChunkAsync(uploadId, idx, buf);
                idx++;
                if (read < cs) break;
            }
            _uploadStatus = "合并中...";
            await _api.CompleteChunkUploadAsync(uploadId);
            _uploadProgress = 1;
            _uploadStatus = "上传完成";
        }
        finally
        {
            await Task.Delay(1500);
            _showUploadPanel = false;
            _page = 1; await FetchAsync(true);
        }
    }
}
