using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StoraDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly StoraApiClient _apiClient;
    private int _currentPage = 1;
    private int _totalCount;
    private const int PerPage = 50;

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private string? _currentFolderId;

    [ObservableProperty]
    private bool _isLoading;

    // 新增：加载更多中
    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private UserInfo? _currentUser;

    [ObservableProperty]
    private FileItem? _selectedFile;

    public bool HasMoreFiles => _currentPage * PerPage < _totalCount;

    public ObservableCollection<FileItem> Files { get; } = new();

    public event EventHandler? LogoutSucceeded;

    public MainViewModel(StoraApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            CurrentUser = await _apiClient.GetCurrentUserAsync();
            await LoadFilesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadFilesAsync()
    {
        _currentPage = 1;
        await FetchPageAsync(clearFirst: true);
    }

    [RelayCommand]
    private async Task LoadMoreFilesAsync()
    {
        if (!HasMoreFiles || IsLoadingMore) return;
        _currentPage++;
        await FetchPageAsync(clearFirst: false);
    }

    private async Task FetchPageAsync(bool clearFirst)
    {
        try
        {
            IsLoadingMore = _currentPage > 1;
            if (_currentPage == 1) IsLoading = true;
            StatusText = _currentPage == 1 ? "正在加载..." : $"正在加载更多... ({_currentPage * PerPage - PerPage} / {_totalCount})";

            var result = await _apiClient.GetFilesAsync(CurrentFolderId, _currentPage, PerPage);
            _totalCount = result.Total;

            if (clearFirst) Files.Clear();
            foreach (var file in result.Items)
                Files.Add(file);

            StatusText = HasMoreFiles
                ? $"已加载 {Files.Count} / {_totalCount} 个项目"
                : $"共 {_totalCount} 个项目";
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(FileItem folder)
    {
        if (!folder.IsFolder) return;
        CurrentFolderId = folder.Id.ToString();
        CurrentPath += $"/{folder.Name}";
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        CurrentFolderId = null;
        CurrentPath = "/";
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _apiClient.LogoutAsync();
        LogoutSucceeded?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task UploadFileAsync()
    {
        try
        {
            IsLoading = true;
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            StatusText = $"正在上传 {files.Count} 个文件...";
            foreach (var file in files)
            {
                using var stream = await file.OpenStreamForReadAsync();
                await _apiClient.UploadFileAsync(stream, file.Name, CurrentFolderId);
            }
            StatusText = "上传完成";
            await LoadFilesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync(FileItem? file)
    {
        if (file == null) return;
        try
        {
            StatusText = $"正在下载 {file.Name}...";
            var savePicker = new FileSavePicker();
            savePicker.SuggestedFileName = file.Name ?? "download";
            savePicker.FileTypeChoices.Add("所有文件", new List<string> { "." });

            var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile == null) return;

            var stream = await _apiClient.DownloadFileAsync(file.Id.ToString());
            using var fileStream = await saveFile.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fileStream);

            StatusText = $"{file.Name} 下载完成";
        }
        catch (Exception ex)
        {
            StatusText = $"下载失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteFileAsync(FileItem? file)
    {
        if (file == null) return;
        try
        {
            StatusText = $"正在删除 {file.Name}...";
            await _apiClient.DeleteFileAsync(file.Id.ToString());
            StatusText = $"{file.Name} 已移入回收站";
            await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ShowPropertiesAsync(FileItem? file)
    {
        if (file == null) return;
        var msg = $"文件名: {file.Name}\n"
                + $"大小: {file.SizeDisplay}\n"
                + $"类型: {(file.IsFolder ? "文件夹" : file.MimeType ?? "未知")}\n"
                + $"创建时间: {file.CreatedAt}\n"
                + $"修改时间: {file.UpdatedAt}";

        var dialog = new ContentDialog
        {
            Title = "属性",
            Content = msg,
            CloseButtonText = "确定",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}