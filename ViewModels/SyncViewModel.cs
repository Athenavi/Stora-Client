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
    [ObservableProperty] private string _status = "未配置";
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private int _intervalIndex;
    [ObservableProperty] private int _conflictIndex;
    [ObservableProperty] private int _namingIndex = 0;   // 0=version, 1=overwrite, 2=manual
    [ObservableProperty] private bool _keepVersions = true;
    [ObservableProperty] private int _maxVersions = 10;
    [ObservableProperty] private string _whitelistText = "";
    [ObservableProperty] private string _blacklistText = "*.tmp,*.temp,~$*,Thumbs.db";
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private SyncFileState? _selectedFile;
    [ObservableProperty] private bool _showVersions;

    public int SyncedCount { get; private set; }
    public int TotalCount { get; private set; }
    public int ConflictCount { get; private set; }
    public ObservableCollection<SyncFileState> Files { get; } = new();
    public ObservableCollection<FileVersion> Versions { get; } = new();
    public ObservableCollection<NamingConflict> NamingConflicts { get; } = new();

    public string[] IntervalOptions => new[] { "5 分钟", "10 分钟", "30 分钟" };
    public string[] ConflictOptions => new[] { "本地优先", "云端优先", "手动处理" };
    public string[] NamingOptions => new[] { "自动版本命名 (v2/v3...) ", "直接覆盖", "手动处理" };

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
        _namingIndex = cfg.NamingMode switch { "overwrite" => 1, "manual" => 2, _ => 0 };

        RefreshData();
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

    [RelayCommand]
    public void ShowFileVersions(SyncFileState? file)
    {
        if (file == null) return;
        SelectedFile = file;
        Versions.Clear();
        foreach (var v in file.Versions.OrderByDescending(v => v.Version))
            Versions.Add(v);
        ShowVersions = true;
    }

    [RelayCommand]
    public void CloseVersions() => ShowVersions = false;

    [RelayCommand]
    public async Task ResolveNamingConflictAsync(NamingConflict? conflict)
    {
        if (conflict == null) return;
        // 目前只是记录，用户可手动处理
        conflict.Resolved = true;
        _sync.Store.NamingConflicts.Remove(conflict);
        _sync.SaveStatePublic();
        RefreshData();
    }

    private void SaveConfig()
    {
        var cfg = new SyncConfig
        {
            LocalPath = _localPath,
            IntervalSeconds = _intervalIndex >= 0 ? new[] { 300, 600, 1800 }[_intervalIndex] : 300,
            ConflictMode = _conflictIndex switch { 1 => "cloud", 2 => "manual", _ => "local" },
            NamingMode = _namingIndex switch { 1 => "overwrite", 2 => "manual", _ => "version" },
            KeepVersions = _keepVersions,
            MaxVersions = _maxVersions,
            AutoStart = _autoStart,
            Whitelist = string.IsNullOrWhiteSpace(_whitelistText)
                ? new() : _whitelistText.Split(',').Select(s => s.Trim()).ToList(),
            Blacklist = string.IsNullOrWhiteSpace(_blacklistText)
                ? new() : _blacklistText.Split(',').Select(s => s.Trim()).ToList()
        };
        _sync.UpdateConfig(cfg);
    }

    private void OnStateChanged()
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() => { UpdateStatus(); RefreshData(); });
    }

    private void OnFileStatusChanged(SyncFileState file)
    {
        App.MainAppWindow.DispatcherQueue.TryEnqueue(() => RefreshData());
    }

    private void UpdateStatus()
    {
        IsRunning = _sync.IsRunning;
        if (!_sync.IsConfigured)
            Status = "请先选择同步文件夹";
        else if (_sync.IsRunning)
            Status = $"● 同步中 | 频率: {IntervalOptions[_intervalIndex]} | 冲突: {ConflictOptions[_conflictIndex]} | 命名: {NamingOptions[_namingIndex]}";
        else
            Status = $"○ 已暂停 | 共 {_sync.Store.Files.Count} 个文件 | {ConflictCount} 个冲突";
    }

    private void RefreshData()
    {
        Files.Clear();
        NamingConflicts.Clear();
        SyncedCount = 0;
        ConflictCount = 0;

        foreach (var f in _sync.Store.Files)
        {
            Files.Add(f);
            if (f.Status == "synced") SyncedCount++;
            if (f.Status == "conflict" || f.Status == "rename_conflict") ConflictCount++;
        }

        foreach (var nc in _sync.Store.NamingConflicts.Where(nc => !nc.Resolved))
            NamingConflicts.Add(nc);

        TotalCount = Files.Count;
        OnPropertyChanged(nameof(SyncedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ConflictCount));
    }
}
