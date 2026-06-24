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
    private StoraIndex? _index;
    private Timer? _syncTimer;
    private FileSystemWatcher? _watcher;
    private bool _isRunning;
    private readonly string _statePath;
    private const int BLOCK_SIZE = 4 * 1024 * 1024;

    public SyncStore Store => _store;
    public StoraIndex? Index => _index;
    public bool IsRunning => _isRunning;
    public event Action? StateChanged;
    public event Action<SyncFileState>? FileStatusChanged;

    public SyncService(StoraApiClient api)
    {
        _api = api;
        _statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stora", "sync-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        LoadState();
    }

    private void LoadState()
    {
        try { if (File.Exists(_statePath)) { var json = File.ReadAllText(_statePath); _store = JsonSerializer.Deserialize<SyncStore>(json) ?? new SyncStore(); } }
        catch { _store = new SyncStore(); }
    }

    private void SaveState()
    {
        try { File.WriteAllText(_statePath, JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    private void EnsureIndex()
    {
        if (_index == null && !string.IsNullOrEmpty(_store.Config.LocalPath))
        {
            _index = new StoraIndex(_store.Config.LocalPath);
            InitStoraDir();
        }
    }

    private void InitStoraDir()
    {
        try
        {
            var d = Path.Combine(_store.Config.LocalPath, ".Stora");
            var di = new DirectoryInfo(d);
            if (!di.Exists) di.Create();
            if ((di.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden) di.Attributes |= FileAttributes.Hidden;
            Directory.CreateDirectory(Path.Combine(d, "Objects"));
            Directory.CreateDirectory(Path.Combine(d, "versions"));
            Directory.CreateDirectory(Path.Combine(d, "manifests"));
        }
        catch { }
    }

    private static string ComputeHash(string path)
    {
        try { using var sha = SHA256.Create(); using var f = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLower(); }
        catch { return ""; }
    }

    private static string ComputeHashBytes(byte[] data)
    {
        using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLower();
    }

    private string GetRelativePath(string fullPath)
    {
        var root = _store.Config.LocalPath.TrimEnd('\\', '/') + "\\";
        return fullPath.StartsWith(root) ? fullPath.Substring(root.Length) : fullPath;
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

    private void StoreLocalBlocks(string fullPath, string relPath)
    {
        try
        {
            var fi = new FileInfo(fullPath);
            if (!fi.Exists || fi.Length == 0 || _index == null) return;
            var objDir = Path.Combine(_index.StoraPath, "Objects");
            var manDir = Path.Combine(_index.StoraPath, "manifests");
            var fileHash = ComputeHash(fullPath);
            using var stream = File.OpenRead(fullPath);
            var blocks = new List<object>();
            long offset = 0; int idx = 0;
            while (offset < fi.Length)
            {
                var sz = (int)Math.Min(BLOCK_SIZE, fi.Length - offset);
                var buf = new byte[sz]; stream.Read(buf, 0, sz);
                var h = ComputeHashBytes(buf);
                _index.StoreBlock(h, buf);
                blocks.Add(new { index = idx, hash = h, offset, size = sz });
                offset += sz; idx++;
            }
            var man = new { file_hash = fileHash, file_size = fi.Length, block_size = BLOCK_SIZE, blocks, synced_at = DateTime.UtcNow.ToString("O") };
            File.WriteAllText(Path.Combine(manDir, fileHash + ".json"), JsonSerializer.Serialize(man, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private HashSet<string> GetLocalBlockHashes(string fileHash)
    {
        if (_index == null) return new HashSet<string>();
        var manPath = Path.Combine(_index.StoraPath, "manifests", fileHash + ".json");
        if (!File.Exists(manPath)) return new HashSet<string>();
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(manPath));
            return doc.RootElement.GetProperty("blocks").EnumerateArray()
                .Select(b => b.GetProperty("hash").GetString()).Where(h => h != null).ToHashSet()!;
        }
        catch { return new HashSet<string>(); }
    }

    private async Task<HashSet<string>> GetCloudBlockHashes(long cloudId)
    {
        try
        {
            var json = await _api.GetFileManifestAsync(cloudId);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (!root.TryGetProperty("blocks", out var blocks)) return new HashSet<string>();
            return blocks.EnumerateArray().Select(b => b.GetProperty("hash").GetString()).Where(h => h != null).ToHashSet()!;
        }
        catch { return new HashSet<string>(); }
    }

    public void UpdateConfig(SyncConfig config) { _store.Config = config; SaveState(); EnsureIndex(); if (_isRunning) { Stop(); _ = StartAsync(); } }
    public bool IsConfigured => !string.IsNullOrEmpty(_store.Config.LocalPath) && Directory.Exists(_store.Config.LocalPath);

    public async Task StartAsync()
    {
        if (_isRunning || !IsConfigured) return;
        EnsureIndex();
        _isRunning = true;
        await EnsureSyncRootAsync();
        await FullSyncAsync();
        var ms = Math.Max(_store.Config.IntervalSeconds, 60) * 1000;
        _syncTimer = new Timer(async _ => await PollCloudAsync(), null, ms, ms);
        StartWatcher();
        StateChanged?.Invoke();
    }

    public void Stop() { _isRunning = false; _syncTimer?.Dispose(); _syncTimer = null; StopWatcher(); SaveState(); _index?.Dispose(); _index = null; StateChanged?.Invoke(); }

    private void StartWatcher()
    {
        if (!IsConfigured) return;
        try
        {
            _watcher = new FileSystemWatcher(_store.Config.LocalPath) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _watcher.Created += (s, e) => _ = OnLocalChangeAsync(e.FullPath);
            _watcher.Changed += (s, e) => _ = OnLocalChangeAsync(e.FullPath);
            _watcher.Deleted += (s, e) => _ = OnLocalDeleteAsync(e.FullPath);
            _watcher.Renamed += (s, e) => _ = OnLocalRenameAsync(e.OldFullPath, e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void StopWatcher() { _watcher?.Dispose(); _watcher = null; }

    private async Task EnsureSyncRootAsync()
    {
        try { var f = await _api.CreateFolderByPathAsync("Sync"); _store.Config.CloudFolderId = f.Id.ToString(); SaveState(); }
        catch { }
    }

    private async Task<long> EnsureParentFolderAsync(string rel)
    {
        var dir = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
        if (string.IsNullOrEmpty(dir)) return long.TryParse(_store.Config.CloudFolderId, out var r) ? r : 0;
        long pid = long.TryParse(_store.Config.CloudFolderId, out var rid) ? rid : 0;
        foreach (var seg in dir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var c = await _api.CreateFolderAsync(seg, pid > 0 ? pid.ToString() : null); pid = c.Id; } catch { }
        }
        return pid;
    }

    private async Task FullSyncAsync()
    {
        if (!IsConfigured || _index == null) return;

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

        var dbFiles = _index.GetAllFiles();
        var dbPaths = new HashSet<string>(dbFiles.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
        var changes = new List<(string path, string hash, string action)>();

        foreach (var (rel, (mtime, size, hash)) in localFiles)
        {
            if (!dbPaths.Contains(rel))
            {
                _index.UpsertFile(rel, Path.GetFileName(rel), hash, size, mtime.ToString("O"));
                changes.Add((rel, hash, "created"));
                _index.AppendJournal(rel, "created", hash, size);
            }
            else
            {
                var oldHash = _index.GetHash(rel);
                if (oldHash != hash)
                {
                    _index.UpsertFile(rel, Path.GetFileName(rel), hash, size, mtime.ToString("O"));
                    changes.Add((rel, hash, "modified"));
                    _index.AppendJournal(rel, "modified", hash, size);
                }
            }
        }

        foreach (var (path, hash, cloudId) in dbFiles)
        {
            if (!localFiles.ContainsKey(path))
            {
                _index.RemoveFile(path);
                changes.Add((path, hash, "deleted"));
                _index.AppendJournal(path, "deleted", hash);
                _store.Files.RemoveAll(f => f.LocalPath == path);
                if (cloudId > 0) { try { await _api.DeleteFileAsync(cloudId.ToString()); } catch { } }
            }
        }

        if (changes.Count > 0) _index.CreateSnapshot(string.Join(", ", changes.Select(c => $"{c.action}:{c.path}").Take(5)), changes);

        SaveState();

        var pending = _index.GetPendingFiles();
        foreach (var (path, hash, cloudId) in pending)
        {
            var fullPath = Path.Combine(_store.Config.LocalPath, path);
            if (File.Exists(fullPath)) await UploadFileAsync(path, hash, cloudId > 0 ? cloudId : null);
        }

        _store.LastSync = DateTime.UtcNow; SaveState(); StateChanged?.Invoke();
    }

    private async Task UploadFileAsync(string relPath, string hash, long? existingCloudId)
    {
        var fullPath = Path.Combine(_store.Config.LocalPath, relPath);
        if (!File.Exists(fullPath) || _index == null) return;

        try
        {
            StoreLocalBlocks(fullPath, relPath);
            var fileHash = ComputeHash(fullPath);
            var localBlocks = GetLocalBlockHashes(fileHash);
            if (localBlocks.Count == 0) return;

            var cloudBlocks = existingCloudId != null && existingCloudId > 0
                ? await GetCloudBlockHashes(existingCloudId.Value) : new HashSet<string>();

            var toUpload = localBlocks.Where(b => !cloudBlocks.Contains(b)).ToList();
            foreach (var blockHash in toUpload)
            {
                var sub = Path.Combine(_index.StoraPath, "Objects", blockHash.Substring(0, 2));
                var bp = Path.Combine(sub, blockHash.Substring(2));
                if (File.Exists(bp)) await _api.UploadBlockAsync(File.ReadAllBytes(bp));
            }

            var parentId = await EnsureParentFolderAsync(relPath);
            var parent = parentId > 0 ? parentId.ToString() : _store.Config.CloudFolderId;
            var len = new FileInfo(fullPath).Length;

            long cloudId;
            if (existingCloudId != null && existingCloudId > 0)
            {
                using var s = File.OpenRead(fullPath); await _api.UpdateFileContentAsync(existingCloudId.Value, s, Path.GetFileName(relPath));
                cloudId = existingCloudId.Value;
            }
            else if (len > 10 * 1024 * 1024)
            {
                var uid = await _api.InitChunkUploadAsync(Path.GetFileName(relPath), len, parent);
                const int cs = 4 * 1024 * 1024;
                var total = (int)Math.Ceiling((double)len / cs);
                using var stream = File.OpenRead(fullPath);
                var buf = new byte[cs];
                for (int i = 0; i < total; i++)
                {
                    if (!_isRunning) return;
                    var r = (int)Math.Min(cs, len - i * cs); if (r < cs) buf = new byte[r];
                    await stream.ReadAsync(buf, 0, r); await _api.UploadChunkAsync(uid, i, buf);
                }
                cloudId = await _api.CompleteChunkUploadAsync(uid);
            }
            else
            {
                using var s = File.OpenRead(fullPath); var u = await _api.UploadFileAsync(s, Path.GetFileName(relPath), parent); cloudId = u.Id;
            }

            _index.MarkSynced(relPath, cloudId, hash);
            _index.AppendJournal(relPath, "synced", hash, len);
        }
        catch (Exception ex) { _index.AppendJournal(relPath, "error", ex.Message); }
    }

    private async Task PollCloudAsync()
    {
        if (!_isRunning || _index == null) return;
        try
        {
            var cf = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var c in cf.Items.Where(x => !x.IsFolder))
            {
                if (_index.GetCloudId(c.Name) != null) continue;
                var fullPath = Path.Combine(_store.Config.LocalPath, c.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var stream = await _api.DownloadFileAsync(c.Id.ToString());
                using var fs = File.Create(fullPath); await stream.CopyToAsync(fs);
                var hash = ComputeHash(fullPath);
                _index.UpsertFile(c.Name, c.Name, hash, c.Size ?? 0, DateTime.UtcNow.ToString("O"));
                _index.MarkSynced(c.Name, c.Id, hash);
                _index.AppendJournal(c.Name, "downloaded", hash, c.Size ?? 0);
                StoreLocalBlocks(fullPath, c.Name);
            }
            SaveState(); StateChanged?.Invoke();
        }
        catch { }
    }

    private async Task OnLocalChangeAsync(string fullPath)
    {
        if (!_isRunning || !File.Exists(fullPath) || _index == null) return;
        var rel = GetRelativePath(fullPath);
        if (!ShouldSync(Path.GetFileName(rel))) return;
        var hash = ComputeHash(fullPath);
        var info = new FileInfo(fullPath);
        var oldHash = _index.GetHash(rel);
        if (oldHash != hash)
        {
            _index.UpsertFile(rel, Path.GetFileName(rel), hash, info.Length, info.LastWriteTimeUtc.ToString("O"));
            _index.AppendJournal(rel, "modified", hash, info.Length);
            var cloudId = _index.GetCloudId(rel);
            await UploadFileAsync(rel, hash, cloudId);
            _index.CreateSnapshot($"modified: {Path.GetFileName(rel)}", new List<(string, string, string)> { (rel, hash, "modified") });
        }
        StateChanged?.Invoke();
    }

    private async Task OnLocalDeleteAsync(string fullPath)
    {
        if (!_isRunning || _index == null) return;
        var rel = GetRelativePath(fullPath);
        var cloudId = _index.GetCloudId(rel);
        _index.AppendJournal(rel, "deleted", _index.GetHash(rel) ?? "");
        _index.RemoveFile(rel);
        _store.Files.RemoveAll(f => f.LocalPath == rel);
        if (cloudId != null && cloudId > 0) { try { await _api.DeleteFileAsync(cloudId.ToString()); } catch { } }
        _index.CreateSnapshot($"deleted: {Path.GetFileName(rel)}", new List<(string, string, string)> { (rel, "", "deleted") });
        StateChanged?.Invoke();
    }

    private async Task OnLocalRenameAsync(string oldPath, string newPath)
    {
        if (!_isRunning || _index == null) return;
        var oldRel = GetRelativePath(oldPath); var newRel = GetRelativePath(newPath);
        var cloudId = _index.GetCloudId(oldRel);
        _index.AppendJournal(oldRel, "renamed", newRel);
        _index.RemoveFile(oldRel);
        _index.UpsertFile(newRel, Path.GetFileName(newRel), _index.GetHash(oldRel) ?? "", new FileInfo(newPath).Length, DateTime.UtcNow.ToString("O"));
        if (cloudId != null) _index.MarkSynced(newRel, cloudId.Value, _index.GetHash(newRel) ?? "");
        _index.CreateSnapshot($"renamed: {Path.GetFileName(oldPath)} -> {Path.GetFileName(newPath)}", new List<(string, string, string)> { (oldRel, "", "deleted"), (newRel, _index.GetHash(newRel) ?? "", "created") });
    }

    /// <summary>
    /// 移除 _store.Files 中实际磁盘上不存在的幽灵文件
    /// </summary>
    public void ClearGhostFiles()
    {
        if (string.IsNullOrEmpty(_store.Config.LocalPath)) return;
        var ghosts = _store.Files
            .Where(f => !File.Exists(Path.Combine(_store.Config.LocalPath, f.LocalPath)))
            .ToList();
        foreach (var g in ghosts)
        {
            _store.Files.Remove(g);
            _index?.RemoveFile(g.LocalPath);
            _index?.AppendJournal(g.LocalPath, "ghost_removed", g.LocalHash ?? "");
        }
        SaveState();
        StateChanged?.Invoke();
    }

    public void SaveStatePublic() => SaveState();
    public async Task ResolveConflictAsync(SyncFileState file, string mode)
    {
        if (mode == "delete")
        {
            if (file.CloudId != null) try { await _api.DeleteFileAsync(file.CloudId.ToString()); } catch { }
            var lp = Path.Combine(_store.Config.LocalPath, file.LocalPath); try { if (File.Exists(lp)) File.Delete(lp); } catch { }
            _store.Files.Remove(file);
        }
        SaveState(); StateChanged?.Invoke();
    }
}
