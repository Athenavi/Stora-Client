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
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StoraDesktop.ViewModels;

public partial class SyncViewModel : ObservableObject
{
    private readonly SyncService _sync;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = "未配置";
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private int _intervalIndex;
    [ObservableProperty] private int _conflictIndex;
    [ObservableProperty] private string _whitelistText = "";
    [ObservableProperty] private string _blacklistText = "*.tmp,*.temp,~$*,Thumbs.db";
    [ObservableProperty] private bool _autoStart;

    public int SyncedCount { get; private set; }
    public int TotalCount { get; private set; }
    public bool IsConfigured => !string.IsNullOrEmpty(_localPath) && Directory.Exists(_localPath);

    public ObservableCollection<SyncFileState> Files { get; } = new();

    private static readonly int[] Intervals = { 300, 600, 1800 };
    public string[] IntervalOptions => new[] { "5 分钟", "10 分钟", "30 分钟" };
    public string[] ConflictOptions => new[] { "本地优先", "云端优先", "手动处理" };

    public SyncViewModel(SyncService sync)
    {
        _sync = sync;
        _sync.StateChanged += OnStateChanged;
        _sync.FileStatusChanged += OnFileStatusChanged;

        var cfg = _sync.Store.Config;
        _localPath = cfg.LocalPath;
        _autoStart = cfg.AutoStart;
        _whitelistText = string.Join(",", cfg.Whitelist);
        _blacklistText = string.Join(",", cfg.Blacklist);
        _intervalIndex = Math.Max(0, Array.IndexOf(Intervals, cfg.IntervalSeconds));
        _conflictIndex = cfg.ConflictMode switch { "cloud" => 1, "manual" => 2, _ => 0 };

        RefreshFileList();
        UpdateStatus();
    }

    [RelayCommand]
    public async Task SelectFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            LocalPath = folder.Path;
            SaveConfig();
        }
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
            IntervalSeconds = _intervalIndex >= 0 ? Intervals[_intervalIndex] : 300,
            ConflictMode = _conflictIndex switch { 1 => "cloud", 2 => "manual", _ => "local" },
            AutoStart = _autoStart,
            Whitelist = string.IsNullOrWhiteSpace(_whitelistText)
                ? new() : _whitelistText.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList(),
            Blacklist = string.IsNullOrWhiteSpace(_blacklistText)
                ? new() : _blacklistText.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()
        };
        _sync.UpdateConfig(cfg);
    }

    private void OnStateChanged()
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() => { UpdateStatus(); RefreshFileList(); });
    }

    private void OnFileStatusChanged(SyncFileState file)
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() => RefreshFileList());
    }

    private void UpdateStatus()
    {
        IsRunning = _sync.IsRunning;
        if (!IsConfigured)
            Status = "请先选择同步文件夹";
        else if (_sync.IsRunning)
            Status = $"● 同步中 | 频率: {IntervalOptions[_intervalIndex]} | 模式: {ConflictOptions[_conflictIndex]}";
        else
            Status = $"○ 已暂停 | 共 {_sync.Store.Files.Count} 个文件";
    }

    private void RefreshFileList()
    {
        Files.Clear();
        SyncedCount = 0;
        foreach (var f in _sync.Store.Files)
        {
            Files.Add(f);
            if (f.Status == "synced") SyncedCount++;
        }
        TotalCount = Files.Count;
        OnPropertyChanged(nameof(SyncedCount));
        OnPropertyChanged(nameof(TotalCount));
    }
}
