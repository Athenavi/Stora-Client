using StoraDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

    /// <summary>
    /// 供外部（ViewModel）调用的保存
    /// </summary>
    public void SaveStatePublic()
    {
        SaveState();
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
        SaveState();
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
            _watcher.Created += (s, e) => _ = OnLocalChangeAsync(e.FullPath);
            _watcher.Changed += (s, e) => _ = OnLocalChangeAsync(e.FullPath);
            _watcher.Deleted += (s, e) => _ = OnLocalDeleteAsync(e.FullPath);
            _watcher.Renamed += (s, e) => _ = OnLocalRenameAsync(e.OldFullPath, e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void StopWatcher() { _watcher?.Dispose(); _watcher = null; }

    #region 过滤与路径

    private bool ShouldSync(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        foreach (var p in _store.Config.Blacklist)
        {
            if (p.StartsWith("*.") && name.EndsWith(p.Substring(1), StringComparison.OrdinalIgnoreCase)) return false;
            if (p.StartsWith("~") && name.StartsWith(p.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals(p, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (_store.Config.Whitelist.Count > 0)
        {
            foreach (var p in _store.Config.Whitelist)
            {
                if (p.StartsWith("*.") && name.EndsWith(p.Substring(1), StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
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

    private static string ComputeHash(string path)
    {
        try { using var s = SHA256.Create(); using var f = File.OpenRead(path); var h = s.ComputeHash(f); return BitConverter.ToString(h).Replace("-","").ToLower(); }
        catch { return ""; }
    }

    private string ResolveFileName(string dir, string name)
    {
        if (!File.Exists(Path.Combine(dir, name))) return name;
        var ext = Path.GetExtension(name);
        var bare = Path.GetFileNameWithoutExtension(name);
        for (int v = 2; v <= 999; v++)
        {
            var n = $"{bare} (v{v}){ext}";
            if (!File.Exists(Path.Combine(dir, n))) return n;
        }
        return $"{bare} (冲突 {DateTime.Now:yyyyMMddHHmmss}){ext}";
    }

    private void BackupLocalFile(SyncFileState file, string reason = "sync")
    {
        if (!_store.Config.KeepVersions) return;
        var full = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        if (!File.Exists(full)) return;
        var ver = file.BackupVersion(_store.Config.LocalPath, reason);
        var bakDir = Path.Combine(_store.Config.LocalPath, ".stora-versions");
        Directory.CreateDirectory(bakDir);
        try
        {
            var bp = Path.Combine(bakDir, $"{file.FileName}.v{ver.Version}.{DateTime.UtcNow:yyyyMMddHHmmss}");
            File.Copy(full, bp); ver.LocalPath = bp;
        }
        catch { }
        while (file.Versions.Count > _store.Config.MaxVersions)
        {
            var o = file.Versions.OrderBy(v => v.Version).First();
            try { if (!string.IsNullOrEmpty(o.LocalPath) && File.Exists(o.LocalPath)) File.Delete(o.LocalPath); } catch { }
            file.Versions.Remove(o);
        }
    }

    #endregion

    private async Task FullSyncAsync()
    {
        if (!IsConfigured) return;
        _store.LastSync = DateTime.UtcNow;
        var localFiles = new Dictionary<string, SyncFileState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Directory.EnumerateFiles(_store.Config.LocalPath, "*", SearchOption.AllDirectories))
            {
                var r = GetRelativePath(p);
                if (r.StartsWith(".stora-versions") || !ShouldSync(r)) continue;
                var i = new FileInfo(p);
                localFiles[r] = new SyncFileState { LocalPath = r, FileName = i.Name, LocalHash = ComputeHash(p), LocalModified = i.LastWriteTimeUtc, Size = i.Length };
            }
        }
        catch { }

        foreach (var e in _store.Files)
        {
            if (localFiles.TryGetValue(e.LocalPath, out var c))
            {
                c.Versions = e.Versions; c.CurrentVersion = e.CurrentVersion; c.CloudId = e.CloudId; c.CloudHash = e.CloudHash;
                if (e.Status == "synced" && e.LocalHash != c.LocalHash) { BackupLocalFile(e); c.Status = "pending"; }
                localFiles[e.LocalPath] = c;
            }
        }
        _store.Files = localFiles.Values.ToList(); SaveState();

        foreach (var f in _store.Files.Where(f => f.Status != "synced" || string.IsNullOrEmpty(f.CloudHash)))
            await UploadFileAsync(f);

        try
        {
            var cf = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var c in cf.Items.Where(c => !c.IsFolder))
            {
                var l = _store.Files.FirstOrDefault(f => f.FileName.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
                if (l == null)
                {
                    var rn = ResolveFileName(_store.Config.LocalPath, c.Name);
                    if (rn != c.Name) RecordNamingConflict(c.Name, rn);
                    _store.Files.Add(new SyncFileState { LocalPath = rn, FileName = rn, CloudId = c.Id, Status = "pending", Size = c.Size ?? 0 });
                }
            }
            foreach (var f in _store.Files.Where(f => f.Status == "pending" && f.CloudId != null))
                await DownloadFileAsync(f);
        }
        catch { }

        _store.LastSync = DateTime.UtcNow; SaveState(); StateChanged?.Invoke();
    }

    private void RecordNamingConflict(string cn, string rn)
    {
        _store.NamingConflicts.Add(new NamingConflict { LocalPath = Path.Combine(_store.Config.LocalPath, cn), CloudName = cn, ResolvedName = rn });
    }

    private async Task PollCloudAsync()
    {
        if (!_isRunning) return;
        try
        {
            var cf = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var c in cf.Items.Where(c => !c.IsFolder))
            {
                var l = _store.Files.FirstOrDefault(f => f.FileName.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
                if (l == null)
                {
                    var rn = ResolveFileName(_store.Config.LocalPath, c.Name);
                    if (rn != c.Name) RecordNamingConflict(c.Name, rn);
                    _store.Files.Add(new SyncFileState { LocalPath = rn, FileName = rn, CloudId = c.Id, Status = "pending", Size = c.Size ?? 0 });
                    await DownloadFileAsync(_store.Files.Last());
                }
            }
            SaveState(); StateChanged?.Invoke();
        }
        catch { }
    }

    private async Task OnLocalChangeAsync(string fullPath)
    {
        if (!_isRunning || !File.Exists(fullPath)) return;
        var rel = GetRelativePath(fullPath);
        if (!ShouldSync(rel)) return;
        var info = new FileInfo(fullPath);
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (e != null) { if (e.LocalHash != ComputeHash(fullPath)) BackupLocalFile(e); e.LocalHash = ComputeHash(fullPath); e.LocalModified = info.LastWriteTimeUtc; e.Size = info.Length; await UploadFileAsync(e); }
        else { _store.Files.Add(new SyncFileState { LocalPath = rel, FileName = info.Name, LocalHash = ComputeHash(fullPath), LocalModified = info.LastWriteTimeUtc, Size = info.Length, Status = "pending" }); await UploadFileAsync(_store.Files.Last()); }
        SaveState(); StateChanged?.Invoke();
    }

    private async Task OnLocalDeleteAsync(string fullPath)
    {
        if (!_isRunning) return;
        var rel = GetRelativePath(fullPath);
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (e?.CloudId != null) { try { await _api.DeleteFileAsync(e.CloudId.ToString()); } catch { } _store.Files.Remove(e); SaveState(); StateChanged?.Invoke(); }
    }

    private async Task OnLocalRenameAsync(string oldPath, string newPath)
    {
        if (!_isRunning) return;
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(GetRelativePath(oldPath), StringComparison.OrdinalIgnoreCase));
        if (e != null) { e.LocalPath = GetRelativePath(newPath); e.FileName = Path.GetFileName(newPath); SaveState(); }
    }

    private async Task UploadFileAsync(SyncFileState file)
    {
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        if (!File.Exists(fullPath)) return;
        try
        {
            file.Status = "syncing"; file.Direction = "upload"; FileStatusChanged?.Invoke(file);

            try
            {
                var cf = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
                var conflict = cf.Items.FirstOrDefault(c => !c.IsFolder && c.Name.Equals(file.FileName, StringComparison.OrdinalIgnoreCase) && c.Id != file.CloudId);
                if (conflict != null)
                {
                    switch (_store.Config.NamingMode)
                    {
                        case "version":
                            var nn = ResolveFileName(Path.GetDirectoryName(fullPath)!, file.FileName);
                            var np = Path.Combine(_store.Config.LocalPath, Path.GetDirectoryName(file.LocalPath) ?? "", nn);
                            File.Copy(fullPath, np); file.LocalPath = Path.Combine(Path.GetDirectoryName(file.LocalPath) ?? "", nn); file.FileName = nn; file.Status = "rename_conflict"; RecordNamingConflict(conflict.Name, nn); SaveState(); FileStatusChanged?.Invoke(file); return;
                        case "overwrite": await _api.DeleteFileAsync(conflict.Id.ToString()); break;
                        default: file.Status = "conflict"; file.Error = $"命名冲突: {conflict.Name}"; FileStatusChanged?.Invoke(file); return;
                    }
                }
            }
            catch { }

            using var s = File.OpenRead(fullPath);
            if (file.CloudId != null)
            {
                // 已有云端 ID → 用内容更新 API（后端自动创建版本快照 + 维护指纹引用计数）
                await _api.UpdateFileContentAsync(file.CloudId.Value, s, file.FileName);
            }
            else
            {
                // 新文件 → 创建
                var u = await _api.UploadFileAsync(s, file.FileName, _store.Config.CloudFolderId);
                file.CloudId = u.Id;
            }
            file.CloudHash = file.LocalHash; file.Status = "synced"; file.Progress = 100; FileStatusChanged?.Invoke(file);
        }
        catch (Exception ex) { file.Status = "error"; file.Error = ex.Message; FileStatusChanged?.Invoke(file); }
    }

    private async Task DownloadFileAsync(SyncFileState file)
    {
        if (file.CloudId == null) return;
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        try
        {
            file.Status = "syncing"; file.Direction = "download"; FileStatusChanged?.Invoke(file);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            if (File.Exists(fullPath) && ComputeHash(fullPath) != file.LocalHash)
            {
                switch (_store.Config.NamingMode)
                {
                    case "version": BackupLocalFile(file, "conflict"); var bn = ResolveFileName(Path.GetDirectoryName(fullPath)!, file.FileName); var bp = Path.Combine(Path.GetDirectoryName(fullPath)!, bn); if (bn != file.FileName) File.Move(fullPath, bp); break;
                    case "overwrite": BackupLocalFile(file, "overwrite"); break;
                    default: file.Status = "conflict"; file.Error = $"本地已存在: {file.FileName}"; FileStatusChanged?.Invoke(file); return;
                }
            }

            using var stream = await _api.DownloadFileAsync(file.CloudId.ToString());
            using var fs = File.Create(fullPath);
            await stream.CopyToAsync(fs);
            file.LocalHash = ComputeHash(fullPath); file.Status = "synced"; file.Progress = 100; FileStatusChanged?.Invoke(file);
        }
        catch (Exception ex) { file.Status = "error"; file.Error = ex.Message; FileStatusChanged?.Invoke(file); }
    }

    public async Task ResolveConflictAsync(SyncFileState file, string mode)
    {
        switch (mode)
        {
            case "local": await UploadFileAsync(file); break;
            case "cloud": await DownloadFileAsync(file); break;
            case "delete":
                if (file.CloudId != null) try { await _api.DeleteFileAsync(file.CloudId.ToString()); } catch { }
                var lp = Path.Combine(_store.Config.LocalPath, file.LocalPath); try { if (File.Exists(lp)) File.Delete(lp); } catch { }
                _store.Files.Remove(file); break;
        }
        SaveState(); StateChanged?.Invoke();
    }
}

