using StoraDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace StoraDesktop.Services;

/// <summary>
/// 同步盘引擎 — 双向同步本地文件夹 ↔ Stora 云端 //Sync 文件夹
/// 自动维护子目录结构、hash 变更自动记录版本、不做自动重命名
/// </summary>
public class SyncService
{
    private readonly StoraApiClient _api;
    private SyncStore _store = new();
    private Timer? _syncTimer;
    private FileSystemWatcher? _watcher;
    private bool _isRunning;
    private readonly string _statePath;
    // 缓存: 相对路径 → 云端文件夹ID
    private readonly Dictionary<string, long> _folderCache = new(StringComparer.OrdinalIgnoreCase);

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
        try
        {
            using var sha = SHA256.Create();
            using var f = File.OpenRead(path);
            var h = sha.ComputeHash(f);
            return BitConverter.ToString(h).Replace("-", "").ToLower();
        }
        catch { return ""; }
    }

    /// <summary>
    /// 确保文件扩展名不在黑名单中
    /// </summary>
    private bool ShouldSync(string name)
    {
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

    // ── 云端文件夹管理 ──

    /// <summary>
    /// 确保 //Sync 根文件夹存在
    /// </summary>
    private async Task EnsureSyncRootAsync()
    {
        try
        {
            var folder = await _api.CreateFolderByPathAsync("Sync");
            var id = folder.Id.ToString();
            if (_store.Config.CloudFolderId != id)
            {
                _store.Config.CloudFolderId = id;
                _folderCache[""] = folder.Id;
                SaveState();
            }
            _folderCache[""] = folder.Id;
        }
        catch { }
    }

    /// <summary>
    /// 确保文件所在子文件夹在云端存在，返回其 folder_id
    /// </summary>
    private async Task<long> EnsureParentFolderAsync(string relativePath)
    {
        var dir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        if (string.IsNullOrEmpty(dir)) return _folderCache.GetValueOrDefault("", 0);

        if (_folderCache.TryGetValue(dir, out var cached)) return cached;

        // 逐级创建
        var segments = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        long parentId = _folderCache.GetValueOrDefault("", 0);
        var currentPath = "";

        foreach (var seg in segments)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? seg : $"{currentPath}/{seg}";
            if (_folderCache.TryGetValue(currentPath, out var existingId))
            {
                parentId = existingId;
                continue;
            }
            try
            {
                var created = await _api.CreateFolderAsync(seg, parentId.ToString());
                parentId = created.Id;
                _folderCache[currentPath] = parentId;
            }
            catch { }
        }
        return parentId;
    }

    // ── 全量同步 ──

    private async Task FullSyncAsync()
    {
        if (!IsConfigured) return;
        _store.LastSync = DateTime.UtcNow;

        // 扫描本地所有文件
        var localFiles = new List<(string relPath, DateTime mtime, long size, string hash)>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_store.Config.LocalPath, "*", SearchOption.AllDirectories))
            {
                var rel = GetRelativePath(path);
                if (rel.StartsWith(".stora-versions") || !ShouldSync(Path.GetFileName(rel))) continue;
                var info = new FileInfo(path);
                localFiles.Add((rel, info.LastWriteTimeUtc, info.Length, ComputeHash(path)));
            }
        }
        catch { }

        // 合并已有状态 + 检测 hash 变化
        var newFiles = new List<SyncFileState>();
        foreach (var (rel, mtime, size, hash) in localFiles)
        {
            var existing = _store.Files.FirstOrDefault(f =>
                f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // 文件已存在 → 检查 hash 是否变化
                if (existing.Status == "synced" && existing.LocalHash != hash)
                {
                    // hash 变了 → 记录版本、标记上传
                    existing.BackupVersion(_store.Config.LocalPath, "sync");
                    existing.LocalHash = hash;
                    existing.LocalModified = mtime;
                    existing.Size = size;
                    existing.Status = "pending";
                }
                else
                {
                    existing.LocalModified = mtime;
                    existing.Size = size;
                    // 保留已有状态，不需要重新上传已同步的
                }
                newFiles.Add(existing);
            }
            else
            {
                // 新文件
                _store.Files.Add(new SyncFileState
                {
                    LocalPath = rel,
                    FileName = Path.GetFileName(rel),
                    LocalHash = hash,
                    LocalModified = mtime,
                    Size = size,
                    Status = "pending"
                });
            }
        }

        // 处理已删除的文件
        var localRelPaths = new HashSet<string>(localFiles.Select(f => f.relPath), StringComparer.OrdinalIgnoreCase);
        var deleted = _store.Files.Where(f => !localRelPaths.Contains(f.LocalPath) && f.Status == "synced").ToList();
        foreach (var d in deleted)
        {
            if (d.CloudId != null)
            {
                try { await _api.DeleteFileAsync(d.CloudId.ToString()); } catch { }
            }
            _store.Files.Remove(d);
        }

        SaveState();

        // 上传待同步文件
        foreach (var file in _store.Files.Where(f => f.Status == "pending"))
            await UploadFileAsync(file);

        SaveState();
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
            // 简化的云端轮询：只下载云端 Sync 目录下新增的文件
            var cloudFiles = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var cf in cloudFiles.Items.Where(c => !c.IsFolder))
            {
                var local = _store.Files.FirstOrDefault(f =>
                    f.FileName.Equals(cf.Name, StringComparison.OrdinalIgnoreCase));
                if (local == null)
                {
                    var newFile = new SyncFileState
                    {
                        LocalPath = cf.Name, // 暂不支持子目录轮询
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

    // ── 本地变化处理 ──

    private async Task OnLocalChangeAsync(string fullPath)
    {
        if (!_isRunning || !File.Exists(fullPath)) return;
        var rel = GetRelativePath(fullPath);
        if (!ShouldSync(Path.GetFileName(rel))) return;

        var hash = ComputeHash(fullPath);
        var info = new FileInfo(fullPath);
        var existing = _store.Files.FirstOrDefault(f =>
            f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (existing.LocalHash != hash)
            {
                // hash 变化 → 记录版本
                existing.BackupVersion(_store.Config.LocalPath, "sync");
                existing.LocalHash = hash;
                existing.LocalModified = info.LastWriteTimeUtc;
                existing.Size = info.Length;
                await UploadFileAsync(existing);
            }
        }
        else
        {
            _store.Files.Add(new SyncFileState
            {
                LocalPath = rel,
                FileName = Path.GetFileName(rel),
                LocalHash = hash,
                LocalModified = info.LastWriteTimeUtc,
                Size = info.Length,
                Status = "pending"
            });
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
        if (e?.CloudId != null)
        {
            try { await _api.DeleteFileAsync(e.CloudId.ToString()); } catch { }
            _store.Files.Remove(e);
            SaveState();
            StateChanged?.Invoke();
        }
    }

    private async Task OnLocalRenameAsync(string oldPath, string newPath)
    {
        if (!_isRunning) return;
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(GetRelativePath(oldPath), StringComparison.OrdinalIgnoreCase));
        if (e != null)
        {
            e.LocalPath = GetRelativePath(newPath);
            e.FileName = Path.GetFileName(newPath);
            SaveState();
        }
    }

    // ── 上传（含子目录支持和版本触发） ──

    private async Task UploadFileAsync(SyncFileState file)
    {
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        if (!File.Exists(fullPath)) return;

        try
        {
            file.Status = "syncing";
            file.Direction = "upload";
            FileStatusChanged?.Invoke(file);

            var parentId = await EnsureParentFolderAsync(file.LocalPath);
            var fileLen = new FileInfo(fullPath).Length;
            var parentFolder = parentId > 0 ? parentId.ToString() : _store.Config.CloudFolderId;

            if (file.CloudId != null)
            {
                // 已有 CloudId → 更新内容（后端自动创建版本快照）
                using var s = File.OpenRead(fullPath);
                await _api.UpdateFileContentAsync(file.CloudId.Value, s, file.FileName);
            }
            else if (fileLen > 10 * 1024 * 1024)
            {
                // 大文件(>10MB) → 分片上传
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
                // 小文件 → 直接上传
                using var s = File.OpenRead(fullPath);
                var u = await _api.UploadFileAsync(s, file.FileName, parentFolder);
                file.CloudId = u.Id;
            }

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

            // 如果本地已有内容不同的文件，先备份版本
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

    // ── 公开方法 ──

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
        SaveState();
        StateChanged?.Invoke();
    }
}
