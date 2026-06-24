using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private string _whitelistText = "";
    [ObservableProperty] private string _blacklistText = "*.tmp,*.temp,~$*,Thumbs.db";
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _buttonText = "Start Sync";

    public string[] IntervalOptions => new[] { "5 min", "10 min", "30 min" };
    public string[] ConflictOptions => new[] { "Local first", "Cloud first", "Manual" };

    public SyncViewModel(SyncService sync)
    {
        _sync = sync;
        _sync.StateChanged += OnStateChanged;

        var cfg = _sync.Store.Config;
        _localPath = cfg.LocalPath;
        _autoStart = cfg.AutoStart;
        _whitelistText = string.Join(",", cfg.Whitelist);
        _blacklistText = string.Join(",", cfg.Blacklist);
        _intervalIndex = Math.Max(0, Array.IndexOf(new[] { 300, 600, 1800 }, cfg.IntervalSeconds));
        _conflictIndex = cfg.ConflictMode switch { "cloud" => 1, "manual" => 2, _ => 0 };
        UpdateStatus();
    }

    [RelayCommand]
    public async Task SelectFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            LocalPath = folder.Path;
            // Register with Windows Explorer overlay
            // Explorer icons via taskbar progress (future: shell extension)
            SaveConfig();
        }
    }

    [RelayCommand]
    public async Task ToggleSyncAsync()
    {
        SaveConfig();
        if (_sync.IsRunning)
        {
            // ShellSyncService cleanup;
            _sync.Stop(); ShellSyncService.ClearTaskbarProgress();
        }
        else
        {
            // ShellSyncService.RegisterSyncRoot(_localPath);
            await _sync.StartAsync(); ShellSyncService.SetTaskbarProgress(50, "Syncing...");
        }
        UpdateStatus();
    }

    private void SaveConfig()
    {
        var cfg = new SyncConfig
        {
            LocalPath = _localPath,
            IntervalSeconds = _intervalIndex >= 0 ? new[] { 300, 600, 1800 }[_intervalIndex] : 300,
            ConflictMode = _conflictIndex switch { 1 => "cloud", 2 => "manual", _ => "local" },
            AutoStart = _autoStart,
            Whitelist = string.IsNullOrWhiteSpace(_whitelistText) ? new() : _whitelistText.Split(',').Select(s => s.Trim()).ToList(),
            Blacklist = string.IsNullOrWhiteSpace(_blacklistText) ? new() : _blacklistText.Split(',').Select(s => s.Trim()).ToList()
        };
        _sync.UpdateConfig(cfg);
    }

    private void OnStateChanged()
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() => UpdateStatus());
    }

    private void UpdateStatus()
    {
        IsRunning = _sync.IsRunning;
        ButtonText = _sync.IsRunning ? "Stop Sync" : "Start Sync";
        if (!_sync.IsConfigured) Status = "Select sync folder first";
        else if (_sync.IsRunning) Status = "Syncing - check status in Explorer";
        else Status = "Sync paused";
    }
}
