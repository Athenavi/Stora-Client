using StoraDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StoraDesktop.Services;

public class SyncService
{
    private readonly StoraApiClient _api;
    private SyncStore _store = new();
    private Timer? _syncTimer;
    private FileSystemWatcher? _watcher;
    private bool _isRunning;
    private readonly string _statePath;
    private readonly object _lock = new();

    public SyncStore Store => _store;
    public bool IsRunning => _isRunning;
    public event Action? StateChanged;
    public event Action<SyncFileState>? FileStatusChanged;

    public SyncService(StoraApiClient api)
    {
        _api = api;
        _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Stora", "sync-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        LoadState();
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                _store = JsonSerializer.Deserialize<SyncStore>(json) ?? new SyncStore();
            }
        }
        catch { _store = new SyncStore(); }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statePath, json);
        }
        catch { }
    }

    public void UpdateConfig(SyncConfig config)
    {
        _store.Config = config;
        SaveState();
        if (_isRunning) { Stop(); _ = StartAsync(); }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_store.Config.LocalPath)
        && Directory.Exists(_store.Config.LocalPath);

    public async Task StartAsync()
    {
        if (_isRunning || !IsConfigured) return;
        _isRunning = true;

        await FullSyncAsync();

        var ms = _store.Config.IntervalSeconds * 1000;
        _syncTimer = new Timer(async _ => await PollCloudAsync(), null, ms, ms);

        StartWatcher();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _isRunning = false;
        _syncTimer?.Dispose();
        _syncTimer = null;
        StopWatcher();
        StateChanged?.Invoke();
    }

    private void StartWatcher()
    {
        if (!IsConfigured) return;
        try
        {
            _watcher = new FileSystemWatcher(_store.Config.LocalPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Created += (s, e) => _ = OnLocalChangeAsync(e.FullPath, "created");
            _watcher.Changed += (s, e) => _ = OnLocalChangeAsync(e.FullPath, "changed");
            _watcher.Deleted += (s, e) => _ = OnLocalDeleteAsync(e.FullPath);
            _watcher.Renamed += (s, e) => _ = OnLocalRenameAsync(e.OldFullPath, e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void StopWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private bool ShouldSync(string relativePath)
    {
        var name = Path.GetFileName(relativePath);

        foreach (var pattern in _store.Config.Blacklist)
        {
            if (pattern.StartsWith("*.") && name.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase))
                return false;
            if (pattern.StartsWith("~") && name.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (_store.Config.Whitelist.Count > 0)
        {
            foreach (var pattern in _store.Config.Whitelist)
            {
                if (pattern.StartsWith("*.") && name.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return true;
    }

    private string GetRelativePath(string fullPath)
    {
        var root = _store.Config.LocalPath.TrimEnd('\\', '/') + "\\";
        return fullPath.StartsWith(root) ? fullPath.Substring(root.Length) : fullPath;
    }

    private static string ComputeHash(string filePath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        catch { return ""; }
    }

    private async Task FullSyncAsync()
    {
        if (!IsConfigured) return;
        _store.LastSync = DateTime.UtcNow;

        var localFiles = new Dictionary<string, SyncFileState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_store.Config.LocalPath, "*", SearchOption.AllDirectories))
            {
                var rel = GetRelativePath(path);
                if (!ShouldSync(rel)) continue;
                var info = new FileInfo(path);
                localFiles[rel] = new SyncFileState
                {
                    LocalPath = rel,
                    FileName = info.Name,
                    LocalHash = ComputeHash(path),
                    LocalModified = info.LastWriteTimeUtc,
                    Size = info.Length,
                    IsFolder = false
                };
            }
        }
        catch { }

        foreach (var existing in _store.Files)
        {
            if (localFiles.TryGetValue(existing.LocalPath, out var current))
            {
                existing.LocalHash = current.LocalHash;
                existing.LocalModified = current.LocalModified;
                existing.Size = current.Size;
                localFiles[existing.LocalPath] = existing;
            }
        }
        _store.Files = localFiles.Values.ToList();
        SaveState();

        foreach (var file in _store.Files.Where(f => f.Status != "synced" || NeedsUpload(f)))
        {
            await UploadFileAsync(file);
        }

        try
        {
            var cloudFiles = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var cf in cloudFiles.Items.Where(cf => !cf.IsFolder))
            {
                var existing = _store.Files.FirstOrDefault(f => f.FileName == cf.Name);
                if (existing == null)
                {
                    var newFile = new SyncFileState
                    {
                        LocalPath = cf.Name,
                        FileName = cf.Name,
                        CloudId = cf.Id,
                        CloudHash = "",
                        Status = "pending",
                        Size = cf.Size ?? 0
                    };
                    _store.Files.Add(newFile);
                    await DownloadFileAsync(newFile);
                }
                else if (!string.IsNullOrEmpty(existing.CloudHash) && existing.CloudHash != cf.Name
                    && existing.Status == "synced" && existing.LocalHash != existing.CloudHash)
                {
                    existing.Status = "conflict";
                    FileStatusChanged?.Invoke(existing);
                }
            }
        }
        catch { }

        _store.LastSync = DateTime.UtcNow;
        SaveState();
        StateChanged?.Invoke();
    }

    private bool NeedsUpload(SyncFileState file) => file.Status == "pending" || file.Status == "error";

    private async Task PollCloudAsync()
    {
        if (!_isRunning) return;
        try
        {
            var cloudFiles = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var cf in cloudFiles.Items.Where(cf => !cf.IsFolder))
            {
                var existing = _store.Files.FirstOrDefault(f => f.FileName == cf.Name);
                if (existing == null)
                {
                    var newFile = new SyncFileState
                    {
                        LocalPath = cf.Name,
                        FileName = cf.Name,
                        CloudId = cf.Id,
                        Status = "pending",
                        Size = cf.Size ?? 0
                    };
                    _store.Files.Add(newFile);
                    await DownloadFileAsync(newFile);
                }
            }
            SaveState();
            StateChanged?.Invoke();
        }
        catch { }
    }

    private async Task OnLocalChangeAsync(string fullPath, string changeType)
    {
        if (!_isRunning) return;
        var rel = GetRelativePath(fullPath);
        if (!ShouldSync(rel) || !File.Exists(fullPath)) return;

        var info = new FileInfo(fullPath);
        var existing = _store.Files.FirstOrDefault(f =>
            f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.LocalHash = ComputeHash(fullPath);
            existing.LocalModified = info.LastWriteTimeUtc;
            existing.Size = info.Length;
            await UploadFileAsync(existing);
        }
        else
        {
            var newFile = new SyncFileState
            {
                LocalPath = rel,
                FileName = info.Name,
                LocalHash = ComputeHash(fullPath),
                LocalModified = info.LastWriteTimeUtc,
                Size = info.Length,
                Status = "pending"
            };
            _store.Files.Add(newFile);
            await UploadFileAsync(newFile);
        }

        SaveState();
        StateChanged?.Invoke();
    }

    private async Task OnLocalDeleteAsync(string fullPath)
    {
        if (!_isRunning) return;
        var rel = GetRelativePath(fullPath);
        var existing = _store.Files.FirstOrDefault(f =>
            f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (existing?.CloudId != null)
        {
            try { await _api.DeleteFileAsync(existing.CloudId.ToString()); } catch { }
            _store.Files.Remove(existing);
            SaveState();
            StateChanged?.Invoke();
        }
    }

    private async Task OnLocalRenameAsync(string oldPath, string newPath)
    {
        if (!_isRunning) return;
        var oldRel = GetRelativePath(oldPath);
        var newRel = GetRelativePath(newPath);
        var existing = _store.Files.FirstOrDefault(f =>
            f.LocalPath.Equals(oldRel, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.LocalPath = newRel;
            existing.FileName = Path.GetFileName(newPath);
            SaveState();
        }
    }

    private async Task UploadFileAsync(SyncFileState file)
    {
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        if (!File.Exists(fullPath)) return;

        try
        {
            file.Status = "syncing";
            file.Direction = "upload";
            FileStatusChanged?.Invoke(file);

            using var stream = File.OpenRead(fullPath);
            var uploaded = await _api.UploadFileAsync(stream, file.FileName, _store.Config.CloudFolderId);
            file.CloudId = uploaded.Id;
            file.CloudHash = file.LocalHash;
            file.Status = "synced";
            file.Progress = 100;
            FileStatusChanged?.Invoke(file);
        }
        catch (Exception ex)
        {
            file.Status = "error";
            file.Error = ex.Message;
            FileStatusChanged?.Invoke(file);
        }
    }

    private async Task DownloadFileAsync(SyncFileState file)
    {
        if (file.CloudId == null) return;
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        try
        {
            file.Status = "syncing";
            file.Direction = "download";
            FileStatusChanged?.Invoke(file);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var stream = await _api.DownloadFileAsync(file.CloudId.ToString());
            using var fs = File.Create(fullPath);
            await stream.CopyToAsync(fs);

            file.LocalHash = ComputeHash(fullPath);
            file.Status = "synced";
            file.Progress = 100;
            FileStatusChanged?.Invoke(file);
        }
        catch (Exception ex)
        {
            file.Status = "error";
            file.Error = ex.Message;
            FileStatusChanged?.Invoke(file);
        }
    }

    public async Task ResolveConflictAsync(SyncFileState file, string mode)
    {
        switch (mode)
        {
            case "local":
                await UploadFileAsync(file);
                break;
            case "cloud":
                await DownloadFileAsync(file);
                break;
            case "delete":
                if (file.CloudId != null)
                    try { await _api.DeleteFileAsync(file.CloudId.ToString()); } catch { }
                var localPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
                try { File.Delete(localPath); } catch { }
                _store.Files.Remove(file);
                break;
        }
        SaveState();
        StateChanged?.Invoke();
    }
}
