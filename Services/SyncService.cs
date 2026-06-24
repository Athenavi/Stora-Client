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
        InitStoraDir();
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

    // ── .Stora 目录初始化 ──

    private void InitStoraDir()
    {
        try
        {
            if (string.IsNullOrEmpty(_store.Config.LocalPath)) return;
            var storaDir = Path.Combine(_store.Config.LocalPath, ".Stora");
            Directory.CreateDirectory(storaDir);
            if ((File.GetAttributes(storaDir) & FileAttributes.Hidden) != FileAttributes.Hidden)
                File.SetAttributes(storaDir, FileAttributes.Hidden | FileAttributes.Directory);
            Directory.CreateDirectory(Path.Combine(storaDir, "Objects"));
            Directory.CreateDirectory(Path.Combine(storaDir, "versions"));
        }
        catch { }
    }

    private void AppendJournal(string relPath, string action, string hash)
    {
        try
        {
            var journalPath = Path.Combine(_store.Config.LocalPath, ".Stora", "journal.jsonl");
            var entry = JsonSerializer.Serialize(new
            {
                time = DateTime.UtcNow.ToString("O"),
                path = relPath,
                action,
                hash,
                size = new FileInfo(Path.Combine(_store.Config.LocalPath, relPath)).Length
            });
            File.AppendAllText(journalPath, entry + Environment.NewLine);
        }
        catch { }
    }

    public void UpdateConfig(SyncConfig config)
    {
        _store.Config = config;
        SaveState();
        InitStoraDir();
        if (_isRunning) { Stop(); _ = StartAsync(); }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_store.Config.LocalPath) && Directory.Exists(_store.Config.LocalPath);

    public async Task StartAsync()
    {
        if (_isRunning || !IsConfigured) return;
        _isRunning = true;
        await EnsureSyncRootAsync();
        await FullSyncAsync();
        var ms = Math.Max(_store.Config.IntervalSeconds, 60) * 1000;
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

    // ── 路径工具 ──

    private string GetRelativePath(string fullPath)
    {
        var root = _store.Config.LocalPath.TrimEnd('\\', '/') + "\\";
        return fullPath.StartsWith(root) ? fullPath.Substring(root.Length) : fullPath;
    }

    private static string ComputeHash(string path)
    {
        try { using var sha = SHA256.Create(); using var f = File.OpenRead(path); var h = sha.ComputeHash(f); return BitConverter.ToString(h).Replace("-", "").ToLower(); }
        catch { return ""; }
    }

    private bool ShouldSync(string name)
    {
        if (name.StartsWith(".Stora")) return false;
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

    // ── 云端文件夹 ──

    private async Task EnsureSyncRootAsync()
    {
        try
        {
            var folder = await _api.CreateFolderByPathAsync("Sync");
            _store.Config.CloudFolderId = folder.Id.ToString();
            SaveState();
        }
        catch { }
    }

    private async Task<long> EnsureParentFolderAsync(string relativePath)
    {
        var dir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        if (string.IsNullOrEmpty(dir)) return long.TryParse(_store.Config.CloudFolderId, out var r) ? r : 0;

        var segments = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        long parentId = long.TryParse(_store.Config.CloudFolderId, out var rootId) ? rootId : 0;

        foreach (var seg in segments)
        {
            try
            {
                var created = await _api.CreateFolderAsync(seg, parentId.ToString());
                parentId = created.Id;
            }
            catch { }
        }
        return parentId;
    }

    // ── 全量同步 ──

    private async Task FullSyncAsync()
    {
        if (!IsConfigured) return;

        // 扫描本地所有文件
        var localFiles = new Dictionary<string, (DateTime mtime, long size, string hash)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_store.Config.LocalPath, "*", SearchOption.AllDirectories))
            {
                var rel = GetRelativePath(path);
                if (!ShouldSync(Path.GetFileName(rel))) continue;
                var info = new FileInfo(path);
                localFiles[rel] = (info.LastWriteTimeUtc, info.Length, ComputeHash(path));
            }
        }
        catch { }

        // 重建 _store.Files（合并已有状态 + 处理删除）
        var merged = new List<SyncFileState>();
        var toDeleteFromCloud = new List<SyncFileState>();

        foreach (var existing in _store.Files)
        {
            if (localFiles.TryGetValue(existing.LocalPath, out var current))
            {
                if (existing.Status == "synced" && existing.LocalHash != current.hash)
                {
                    existing.BackupVersion(_store.Config.LocalPath, "sync");
                    existing.LocalHash = current.hash;
                    existing.LocalModified = current.mtime;
                    existing.Size = current.size;
                    existing.Status = "pending";
                    AppendJournal(existing.LocalPath, "modified", current.hash);
                }
                merged.Add(existing);
                localFiles.Remove(existing.LocalPath);
            }
            else if (existing.CloudId != null && existing.Status == "synced")
            {
                toDeleteFromCloud.Add(existing);
                AppendJournal(existing.LocalPath, "deleted", "");
            }
        }

        // 新增文件
        foreach (var (rel, (mtime, size, hash)) in localFiles)
        {
            merged.Add(new SyncFileState
            {
                LocalPath = rel,
                FileName = Path.GetFileName(rel),
                LocalHash = hash,
                LocalModified = mtime,
                Size = size,
                Status = "pending"
            });
            AppendJournal(rel, "created", hash);
        }

        // 删除云端文件
        foreach (var d in toDeleteFromCloud)
        {
            try { await _api.DeleteFileAsync(d.CloudId.ToString()); } catch { }
        }

        _store.Files = merged;
        _store.LastSync = DateTime.UtcNow;
        SaveState();

        // 上传待同步
        foreach (var file in _store.Files.Where(f => f.Status == "pending"))
            await UploadFileAsync(file);

        _store.LastSync = DateTime.UtcNow;
        SaveState();
        StateChanged?.Invoke();
    }

    // ── 轮询云端 ──

    private async Task PollCloudAsync()
    {
        if (!_isRunning) return;
        try
        {
            var cloudFiles = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var cf in cloudFiles.Items.Where(c => !c.IsFolder))
            {
                if (_store.Files.Any(f => f.FileName.Equals(cf.Name, StringComparison.OrdinalIgnoreCase))) continue;

                _store.Files.Add(new SyncFileState
                {
                    LocalPath = cf.Name,
                    FileName = cf.Name,
                    CloudId = cf.Id,
                    Status = "pending",
                    Size = cf.Size ?? 0
                });
                await DownloadFileAsync(_store.Files.Last());
            }
            SaveState();
            StateChanged?.Invoke();
        }
        catch { }
    }

    // ── 本地事件处理 ──

    private async Task OnLocalChangeAsync(string fullPath)
    {
        if (!_isRunning || !File.Exists(fullPath)) return;
        var rel = GetRelativePath(fullPath);
        if (!ShouldSync(Path.GetFileName(rel))) return;

        var hash = ComputeHash(fullPath);
        var info = new FileInfo(fullPath);
        var existing = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (existing.LocalHash != hash)
            {
                existing.BackupVersion(_store.Config.LocalPath, "sync");
                existing.LocalHash = hash;
                existing.LocalModified = info.LastWriteTimeUtc;
                existing.Size = info.Length;
                AppendJournal(rel, "modified", hash);
                await UploadFileAsync(existing);
            }
        }
        else
        {
            _store.Files.Add(new SyncFileState
            {
                LocalPath = rel, FileName = Path.GetFileName(rel),
                LocalHash = hash, LocalModified = info.LastWriteTimeUtc,
                Size = info.Length, Status = "pending"
            });
            AppendJournal(rel, "created", hash);
            await UploadFileAsync(_store.Files.Last());
        }
        SaveState();
        StateChanged?.Invoke();
    }

    private async Task OnLocalDeleteAsync(string fullPath)
    {
        if (!_isRunning) return;
        var rel = GetRelativePath(fullPath);
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));

        if (e == null) return;

        AppendJournal(rel, "deleted", e.LocalHash);

        if (e.CloudId != null)
        {
            try { await _api.DeleteFileAsync(e.CloudId.ToString()); } catch { }
        }

        _store.Files.Remove(e);
        SaveState();
        StateChanged?.Invoke();
    }

    private async Task OnLocalRenameAsync(string oldPath, string newPath)
    {
        if (!_isRunning) return;
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(GetRelativePath(oldPath), StringComparison.OrdinalIgnoreCase));
        if (e != null)
        {
            AppendJournal(GetRelativePath(oldPath), "deleted", e.LocalHash);
            e.LocalPath = GetRelativePath(newPath);
            e.FileName = Path.GetFileName(newPath);
            AppendJournal(e.LocalPath, "created", e.LocalHash);
            SaveState();
        }
    }

    // ── 上传 ──

    private async Task UploadFileAsync(SyncFileState file)
    {
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        if (!File.Exists(fullPath)) return;

        try
        {
            file.Status = "syncing"; file.Direction = "upload";
            FileStatusChanged?.Invoke(file);

            var parentId = await EnsureParentFolderAsync(file.LocalPath);
            var parentFolder = parentId > 0 ? parentId.ToString() : _store.Config.CloudFolderId;
            var fileLen = new FileInfo(fullPath).Length;

            if (file.CloudId != null)
            {
                using var s = File.OpenRead(fullPath);
                await _api.UpdateFileContentAsync(file.CloudId.Value, s, file.FileName);
            }
            else if (fileLen > 10 * 1024 * 1024)
            {
                var uploadId = await _api.InitChunkUploadAsync(file.FileName, fileLen, parentFolder);
                const int cs = 4 * 1024 * 1024;
                var total = (int)Math.Ceiling((double)fileLen / cs);
                using var stream = File.OpenRead(fullPath);
                var buf = new byte[cs];
                for (int i = 0; i < total; i++)
                {
                    if (!_isRunning) { file.Status = "pending"; FileStatusChanged?.Invoke(file); return; }
                    var read = (int)Math.Min(cs, fileLen - i * cs);
                    if (read < cs) buf = new byte[read];
                    await stream.ReadAsync(buf, 0, read);
                    await _api.UploadChunkAsync(uploadId, i, buf);
                    file.Progress = (int)(100.0 * (i + 1) / total);
                    FileStatusChanged?.Invoke(file);
                }
                file.CloudId = await _api.CompleteChunkUploadAsync(uploadId);
            }
            else
            {
                using var s = File.OpenRead(fullPath);
                var u = await _api.UploadFileAsync(s, file.FileName, parentFolder);
                file.CloudId = u.Id;
            }

            file.CloudHash = file.LocalHash;
            file.Status = "synced"; file.Progress = 100;
            FileStatusChanged?.Invoke(file);
        }
        catch (Exception ex)
        {
            file.Status = "error"; file.Error = ex.Message;
            FileStatusChanged?.Invoke(file);
        }
    }

    // ── 下载 ──

    private async Task DownloadFileAsync(SyncFileState file)
    {
        if (file.CloudId == null) return;
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        try
        {
            file.Status = "syncing"; file.Direction = "download";
            FileStatusChanged?.Invoke(file);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            if (File.Exists(fullPath))
            {
                var localHash = ComputeHash(fullPath);
                if (localHash != file.LocalHash)
                    file.BackupVersion(_store.Config.LocalPath, "sync");
            }

            using var stream = await _api.DownloadFileAsync(file.CloudId.ToString());
            using var fs = File.Create(fullPath);
            await stream.CopyToAsync(fs);

            file.LocalHash = ComputeHash(fullPath);
            file.Status = "synced"; file.Progress = 100;
            FileStatusChanged?.Invoke(file);
        }
        catch (Exception ex)
        {
            file.Status = "error"; file.Error = ex.Message;
            FileStatusChanged?.Invoke(file);
        }
    }

    public void SaveStatePublic() => SaveState();

    public async Task ResolveConflictAsync(SyncFileState file, string mode)
    {
        switch (mode)
        {
            case "local": await UploadFileAsync(file); break;
            case "cloud": await DownloadFileAsync(file); break;
            case "delete":
                if (file.CloudId != null) try { await _api.DeleteFileAsync(file.CloudId.ToString()); } catch { }
                var lp = Path.Combine(_store.Config.LocalPath, file.LocalPath);
                try { if (File.Exists(lp)) File.Delete(lp); } catch { }
                _store.Files.Remove(file); break;
        }
        SaveState(); StateChanged?.Invoke();
    }
}
