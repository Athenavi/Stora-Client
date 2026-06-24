using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StoraDesktop.ViewModels;

public partial class FileViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    private int _page = 1, _total;
    private const int PageSize = 50;
    private UserInfo? _user;
    private static List<long> _clipboardFileIds = new();
    private static string _clipboardAction = "";

    [ObservableProperty] private string _currPath = "/";
    [ObservableProperty] private string? _currFolderId;
    [ObservableProperty] private bool _isLoading, _loadingMore;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private FileItem? _selectedFile;
    [ObservableProperty] private int _selectedCount;
    public ObservableCollection<FileItem> SelectedFiles { get; } = new();
    [ObservableProperty] private bool _showUploadPanel;
    [ObservableProperty] private double _uploadProgress;
    [ObservableProperty] private string _uploadStatus = "";
    [ObservableProperty] private bool _hasClipboard;
    [ObservableProperty] private string _clipboardInfo = "";

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
            _status = HasMore ? $"Loaded {Files.Count}/{_total}" : $"Total {_total}";
        }
        finally { IsLoading = false; _loadingMore = false; }
    }

    [RelayCommand] public async Task LoadMoreAsync()
    {
        if (!HasMore || _loadingMore) return;
        _page++; await FetchAsync(false);
    }

    [RelayCommand]
    public async Task OpenFolderAsync(FileItem? f)
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

    [RelayCommand]
    public async Task DownloadFileAsync(FileItem? f)
    {
        if (f == null) return;
        try
        {
            _status = $"Downloading {f.Name}...";
            var picker = new FileSavePicker();
            picker.SuggestedFileName = f.Name ?? "download";
            picker.FileTypeChoices.Add("All files", new List<string> { "." });
            var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var stream = await _api.DownloadFileAsync(f.Id.ToString());
            using var fs = await file.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fs);
            _status = $"{f.Name} downloaded";
        }
        catch (Exception ex) { _status = $"Download failed: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task ShareFileAsync(FileItem? f)
    {
        if (f == null) return;
        try
        {
            var share = await _api.CreateShareAsync(f.Id);
            var pkg = new DataPackage();
            pkg.SetText(share.ShareUrl);
            Clipboard.SetContent(pkg);
            _status = $"Share link copied: {share.ShareUrl}";
            NavigateToShare?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { _status = $"Share failed: {ex.Message}"; }
    }

    [RelayCommand]
    public void CopyFiles()
    {
        if (SelectedFiles.Count == 0) { _status = "Select files first"; return; }
        _clipboardFileIds = SelectedFiles.Select(f => f.Id).ToList();
        _clipboardAction = "copy";
        HasClipboard = true;
        ClipboardInfo = $"{_clipboardFileIds.Count} file(s) copied";
        _status = ClipboardInfo;
    }

    [RelayCommand]
    public void CutFiles()
    {
        if (SelectedFiles.Count == 0) { _status = "Select files first"; return; }
        _clipboardFileIds = SelectedFiles.Select(f => f.Id).ToList();
        _clipboardAction = "move";
        HasClipboard = true;
        ClipboardInfo = $"{_clipboardFileIds.Count} file(s) cut";
        _status = ClipboardInfo;
    }

    [RelayCommand]
    public async Task PasteFilesAsync()
    {
        if (!HasClipboard || _clipboardFileIds.Count == 0) return;
        try
        {
            var targetId = long.TryParse(_currFolderId, out var tid) ? tid : 0;
            if (_clipboardAction == "copy")
                await _api.BatchCopyAsync(_clipboardFileIds, targetId);
            else
                await _api.BatchMoveAsync(_clipboardFileIds, targetId);
            _status = $"{_clipboardFileIds.Count} file(s) {_clipboardAction} completed";
            HasClipboard = false;
            await InitAsync();
        }
        catch (Exception ex) { _status = $"Paste failed: {ex.Message}"; }
    }

    [RelayCommand]
    public void ClearSelection()
    {
        SelectedFiles.Clear(); SelectedCount = 0;
        _status = "Selection cleared";
    }

    [RelayCommand]
    public void ClearClipboard()
    {
        _clipboardFileIds.Clear();
        HasClipboard = false;
        ClipboardInfo = "";
        _status = "Clipboard cleared";
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (SelectedFiles.Count == 0) { _status = "Select files first"; return; }
        var names = string.Join(", ", SelectedFiles.Take(3).Select(f => f.Name));
        if (SelectedFiles.Count > 3) names += $" and {SelectedFiles.Count - 3} more";
        var confirm = new ContentDialog
        {
            Title = "Delete files?",
            Content = $"Delete {SelectedFiles.Count} file(s): {names}?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var f in SelectedFiles.ToList())
            try { await _api.DeleteFileAsync(f.Id.ToString()); Files.Remove(f); } catch { }
        SelectedFiles.Clear(); SelectedCount = 0;
        _status = "Deleted successfully";
    }

    [RelayCommand]
    public async Task ShareSelectedAsync()
    {
        if (SelectedFiles.Count == 0) { _status = "Select files first"; return; }
        foreach (var f in SelectedFiles)
        {
            try
            {
                var share = await _api.CreateShareAsync(f.Id);
                var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                pkg.SetText(share.ShareUrl);
                Clipboard.SetContent(pkg);
            }
            catch { }
        }
        _status = $"Shared {SelectedFiles.Count} file(s)";
    }

    [RelayCommand]
    public async Task DeleteFileAsync(FileItem? f)
    {
        if (f == null) return;
        await _api.DeleteFileAsync(f.Id.ToString());
        Files.Remove(f);
        _status = $"{f.Name} deleted";
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(FileItem? f)
    {
        if (f == null) return;
        await _api.ToggleFavoriteAsync(f.Id.ToString());
        f.IsFavorite = !f.IsFavorite;
    }

    [RelayCommand]
    public async Task ShowPropsAsync(FileItem? f)
    {
        if (f == null) return;
        var d = new ContentDialog
        {
            Title = "Properties", CloseButtonText = "OK",
            Content = $"Name: {f.Name}\nSize: {f.SizeDisplay}\nType: {(f.IsFolder ? "Folder" : f.MimeType ?? "Unknown")}\nCreated: {f.CreatedAt}",
            XamlRoot = App.MainAppWindow.Content.XamlRoot
        };
        await d.ShowAsync();
    }

    [RelayCommand]
    public async Task AddOfflineDownloadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        await _api.CreateOfflineDownloadAsync(url);
        _status = "Offline download added";
    }

    public async Task HandleDropAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems)) return;
        var items = await data.GetStorageItemsAsync();
        _showUploadPanel = true;
        int ok = 0;
        foreach (var item in items)
        {
            if (item is Windows.Storage.StorageFile file)
            {
                _uploadStatus = $"Uploading {file.Name}...";
                using var s = await file.OpenStreamForReadAsync();
                await _api.UploadFileAsync(s, file.Name, _currFolderId);
                ok++;
            }
        }
        _uploadStatus = $"Done ({ok} files)";
        _page = 1; await FetchAsync(true);
        await Task.Delay(2000);
        _showUploadPanel = false;
    }

    public async Task ChunkedUploadAsync(Windows.Storage.StorageFile file)
    {
        _showUploadPanel = true;
        try
        {
            var props = await file.GetBasicPropertiesAsync();
            var size = (long)props.Size;
            if (size < 10 * 1024 * 1024)
            {
                _uploadStatus = $"Uploading {file.Name}...";
                using var s = await file.OpenStreamForReadAsync();
                await _api.UploadFileAsync(s, file.Name, _currFolderId);
                _uploadStatus = "Done";
                return;
            }
            _uploadStatus = "Init chunk upload...";
            var uploadId = await _api.InitChunkUploadAsync(file.Name, size, _currFolderId);
            const int cs = 4 * 1024 * 1024;
            using var stream = await file.OpenStreamForReadAsync();
            var buf = new byte[cs];
            int idx = 0, total = (int)Math.Ceiling((double)size / cs);
            while (true)
            {
                int read = await stream.ReadAsync(buf);
                if (read == 0) break;
                if (read < cs) Array.Resize(ref buf, read);
                _uploadStatus = $"Chunk {idx + 1}/{total} ({100 * idx / total}%)";
                _uploadProgress = (double)idx / total;
                await _api.UploadChunkAsync(uploadId, idx, buf);
                idx++;
                if (read < cs) break;
            }
            _uploadStatus = "Merging...";
            await _api.CompleteChunkUploadAsync(uploadId);
            _uploadProgress = 1;
            _uploadStatus = "Upload complete";
        }
        finally
        {
            await Task.Delay(1500);
            _showUploadPanel = false;
            _page = 1; await FetchAsync(true);
        }
    }
}
