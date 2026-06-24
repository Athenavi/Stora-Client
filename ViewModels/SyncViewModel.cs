using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;

namespace StoraDesktop.ViewModels;

public partial class SyncViewModel : ObservableObject
{
    private readonly SyncService _sync;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = "Not configured";
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private int _intervalIndex;
    [ObservableProperty] private int _conflictIndex;
    [ObservableProperty] private bool _keepVersions = true;
    [ObservableProperty] private int _maxVersions = 10;
    [ObservableProperty] private string _whitelistText = "";
    [ObservableProperty] private string _blacklistText = "*.tmp,*.temp,~$*,Thumbs.db";
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _buttonText = "Start Sync";

    public int SyncedCount { get; private set; }
    public int TotalCount { get; private set; }
    public bool HasFiles => TotalCount > 0;
    public event Action<string>? FileFlashRequested;

    public ObservableCollection<SyncFileState> Files { get; } = new();
    public string[] IntervalOptions => new[] { "5 min", "10 min", "30 min" };
    public string[] ConflictOptions => new[] { "Local first", "Cloud first", "Manual" };

    public SyncViewModel(SyncService sync)
    {
        _sync = sync;
        _sync.StateChanged += OnStateChanged;
        _sync.FileStatusChanged += OnFileStatusChanged;

        var cfg = _sync.Store.Config;
        _localPath = cfg.LocalPath;
        _autoStart = cfg.AutoStart;
        _keepVersions = cfg.KeepVersions;
        _maxVersions = cfg.MaxVersions;
        _whitelistText = string.Join(",", cfg.Whitelist);
        _blacklistText = string.Join(",", cfg.Blacklist);
        _intervalIndex = Math.Max(0, Array.IndexOf(new[] { 300, 600, 1800 }, cfg.IntervalSeconds));
        _conflictIndex = cfg.ConflictMode switch { "cloud" => 1, "manual" => 2, _ => 0 };
        RefreshFileList(); UpdateStatus();
    }

    [RelayCommand]
    public async Task SelectFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) { LocalPath = folder.Path; SaveConfig(); }
    }

    [RelayCommand]
    public async Task ToggleSyncAsync()
    {
        SaveConfig();
        if (_sync.IsRunning) _sync.Stop();
        else await _sync.StartAsync();
        UpdateStatus();
    }

    private void SaveConfig()
    {
        var cfg = new SyncConfig
        {
            LocalPath = _localPath,
            IntervalSeconds = _intervalIndex >= 0 ? new[] { 300, 600, 1800 }[_intervalIndex] : 300,
            ConflictMode = _conflictIndex switch { 1 => "cloud", 2 => "manual", _ => "local" },
            KeepVersions = _keepVersions,
            MaxVersions = _maxVersions,
            AutoStart = _autoStart,
            Whitelist = string.IsNullOrWhiteSpace(_whitelistText) ? new() : _whitelistText.Split(',').Select(s => s.Trim()).ToList(),
            Blacklist = string.IsNullOrWhiteSpace(_blacklistText) ? new() : _blacklistText.Split(',').Select(s => s.Trim()).ToList()
        };
        _sync.UpdateConfig(cfg);
    }

    private void OnStateChanged()
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() => { UpdateStatus(); RefreshFileList(); });
    }

    private void OnFileStatusChanged(SyncFileState file)
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() =>
        {
            RefreshFileList();
            if (file.Status == "syncing")
                FileFlashRequested?.Invoke(file.FileName);
        });
    }

    private void UpdateStatus()
    {
        IsRunning = _sync.IsRunning;
        ButtonText = _sync.IsRunning ? "Stop Sync" : "Start Sync";
        if (!_sync.IsConfigured) Status = "Select sync folder first";
        else if (_sync.IsRunning) Status = "Syncing...";
        else Status = $"Paused | {_sync.Store.Files.Count} files";
    }

    private void RefreshFileList()
    {
        Files.Clear(); SyncedCount = 0;
        foreach (var f in _sync.Store.Files) { Files.Add(f); if (f.Status == "synced") SyncedCount++; }
        TotalCount = Files.Count;
        OnPropertyChanged(nameof(SyncedCount)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(HasFiles));
    }
}
