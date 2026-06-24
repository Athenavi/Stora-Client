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
    private const int BLOCK_SIZE = 4 * 1024 * 1024;

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
        try { if (File.Exists(_statePath)) { var json = File.ReadAllText(_statePath); _store = JsonSerializer.Deserialize<SyncStore>(json) ?? new SyncStore(); } }
        catch { _store = new SyncStore(); }
    }

    private void SaveState()
    {
        try { File.WriteAllText(_statePath, JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    private void InitStoraDir()
    {
        try
        {
            if (string.IsNullOrEmpty(_store.Config.LocalPath)) return;
            var d = Path.Combine(_store.Config.LocalPath, ".Stora");
            Directory.CreateDirectory(d);
            if ((File.GetAttributes(d) & FileAttributes.Hidden) != FileAttributes.Hidden)
                File.SetAttributes(d, FileAttributes.Hidden | FileAttributes.Directory);
            Directory.CreateDirectory(Path.Combine(d, "Objects"));
            Directory.CreateDirectory(Path.Combine(d, "versions"));
            Directory.CreateDirectory(Path.Combine(d, "manifests"));
        }
        catch { }
    }

    private void AppendJournal(string relPath, string action, string hash)
    {
        try
        {
            var jp = Path.Combine(_store.Config.LocalPath, ".Stora", "journal.jsonl");
            var e = JsonSerializer.Serialize(new { time = DateTime.UtcNow.ToString("O"), path = relPath, action, hash });
            File.AppendAllText(jp, e + Environment.NewLine);
        }
        catch { }
    }

    // ── 本地块存储（同步后写入 .Stora/Objects/） ──

    private void StoreLocalBlocks(string fullPath, string relPath)
    {
        try
        {
            var fi = new FileInfo(fullPath);
            if (!fi.Exists || fi.Length == 0) return;

            var storaDir = Path.Combine(_store.Config.LocalPath, ".Stora");
            var objDir = Path.Combine(storaDir, "Objects");
            var manDir = Path.Combine(storaDir, "manifests");

            var fileHash = ComputeHash(fullPath);
            using var stream = File.OpenRead(fullPath);
            var blocks = new List<object>();
            long offset = 0;
            int idx = 0;

            while (offset < fi.Length)
            {
                var sz = (int)Math.Min(BLOCK_SIZE, fi.Length - offset);
                var buf = new byte[sz];
                stream.Read(buf, 0, sz);

                var hash = ComputeHashBytes(buf);
                var sub = Path.Combine(objDir, hash.Substring(0, 2));
                Directory.CreateDirectory(sub);
                var bp = Path.Combine(sub, hash.Substring(2));
                if (!File.Exists(bp)) File.WriteAllBytes(bp, buf);

                blocks.Add(new { index = idx, hash, offset, size = sz });
                offset += sz;
                idx++;
            }

            var man = new
            {
                file_hash = fileHash,
                file_size = fi.Length,
                block_size = BLOCK_SIZE,
                blocks,
                synced_at = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(Path.Combine(manDir, fileHash + ".json"),
                JsonSerializer.Serialize(man, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string ComputeHashBytes(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLower();
    }

    public void UpdateConfig(SyncConfig config) { _store.Config = config; SaveState(); InitStoraDir(); if (_isRunning) { Stop(); _ = StartAsync(); } }
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

    public void Stop() { _isRunning = false; _syncTimer?.Dispose(); _syncTimer = null; StopWatcher(); SaveState(); StateChanged?.Invoke(); }

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

    private string GetRelativePath(string fullPath) { var root = _store.Config.LocalPath.TrimEnd('\\', '/') + "\\"; return fullPath.StartsWith(root) ? fullPath.Substring(root.Length) : fullPath; }

    private static string ComputeHash(string path)
    {
        try { using var sha = SHA256.Create(); using var f = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLower(); }
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
            try { var c = await _api.CreateFolderAsync(seg, pid.ToString()); pid = c.Id; }
            catch { }
        }
        return pid;
    }

    private async Task FullSyncAsync()
    {
        if (!IsConfigured) return;

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

        var merged = new List<SyncFileState>();
        var toDel = new List<SyncFileState>();

        foreach (var existing in _store.Files)
        {
            if (localFiles.TryGetValue(existing.LocalPath, out var cur))
            {
                if (existing.Status == "synced" && existing.LocalHash != cur.hash)
                {
                    existing.BackupVersion(_store.Config.LocalPath, "sync");
                    existing.LocalHash = cur.hash; existing.LocalModified = cur.mtime; existing.Size = cur.size; existing.Status = "pending";
                    AppendJournal(existing.LocalPath, "modified", cur.hash);
                }
                merged.Add(existing);
                localFiles.Remove(existing.LocalPath);
            }
            else if (existing.CloudId != null && existing.Status == "synced")
            {
                toDel.Add(existing);
                AppendJournal(existing.LocalPath, "deleted", "");
            }
        }

        foreach (var (rel, (mtime, size, hash)) in localFiles)
        {
            merged.Add(new SyncFileState { LocalPath = rel, FileName = Path.GetFileName(rel), LocalHash = hash, LocalModified = mtime, Size = size, Status = "pending" });
            AppendJournal(rel, "created", hash);
        }

        foreach (var d in toDel) { try { await _api.DeleteFileAsync(d.CloudId.ToString()); } catch { } }

        _store.Files = merged; _store.LastSync = DateTime.UtcNow; SaveState();

        foreach (var file in _store.Files.Where(f => f.Status == "pending"))
            await UploadFileAsync(file);

        _store.LastSync = DateTime.UtcNow; SaveState(); StateChanged?.Invoke();
    }

    private async Task PollCloudAsync()
    {
        if (!_isRunning) return;
        try
        {
            var cf = await _api.GetFilesAsync(_store.Config.CloudFolderId, perPage: 200);
            foreach (var c in cf.Items.Where(c => !c.IsFolder))
            {
                if (_store.Files.Any(f => f.FileName.Equals(c.Name, StringComparison.OrdinalIgnoreCase))) continue;
                _store.Files.Add(new SyncFileState { LocalPath = c.Name, FileName = c.Name, CloudId = c.Id, Status = "pending", Size = c.Size ?? 0 });
                await DownloadFileAsync(_store.Files.Last());
            }
            SaveState(); StateChanged?.Invoke();
        }
        catch { }
    }

    private async Task OnLocalChangeAsync(string fullPath)
    {
        if (!_isRunning || !File.Exists(fullPath)) return;
        var rel = GetRelativePath(fullPath);
        if (!ShouldSync(Path.GetFileName(rel))) return;
        var hash = ComputeHash(fullPath);
        var info = new FileInfo(fullPath);
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (e != null)
        {
            if (e.LocalHash != hash) { e.BackupVersion(_store.Config.LocalPath, "sync"); e.LocalHash = hash; e.LocalModified = info.LastWriteTimeUtc; e.Size = info.Length; AppendJournal(rel, "modified", hash); await UploadFileAsync(e); }
        }
        else
        {
            _store.Files.Add(new SyncFileState { LocalPath = rel, FileName = Path.GetFileName(rel), LocalHash = hash, LocalModified = info.LastWriteTimeUtc, Size = info.Length, Status = "pending" });
            AppendJournal(rel, "created", hash); await UploadFileAsync(_store.Files.Last());
        }
        SaveState(); StateChanged?.Invoke();
    }

    private async Task OnLocalDeleteAsync(string fullPath)
    {
        if (!_isRunning) return;
        var rel = GetRelativePath(fullPath);
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (e == null) return;
        AppendJournal(rel, "deleted", e.LocalHash);
        if (e.CloudId != null) { try { await _api.DeleteFileAsync(e.CloudId.ToString()); } catch { } }
        _store.Files.Remove(e); SaveState(); StateChanged?.Invoke();
    }

    private async Task OnLocalRenameAsync(string oldPath, string newPath)
    {
        if (!_isRunning) return;
        var e = _store.Files.FirstOrDefault(f => f.LocalPath.Equals(GetRelativePath(oldPath), StringComparison.OrdinalIgnoreCase));
        if (e != null) { AppendJournal(GetRelativePath(oldPath), "deleted", e.LocalHash); e.LocalPath = GetRelativePath(newPath); e.FileName = Path.GetFileName(newPath); AppendJournal(e.LocalPath, "created", e.LocalHash); SaveState(); }
    }

    private async Task UploadFileAsync(SyncFileState file)
    {
        var fullPath = Path.Combine(_store.Config.LocalPath, file.LocalPath);
        if (!File.Exists(fullPath)) return;
        try
        {
            file.Status = "syncing"; file.Direction = "upload"; FileStatusChanged?.Invoke(file);

            // 先创建本地块存储
            StoreLocalBlocks(fullPath, file.LocalPath);

            var parentId = await EnsureParentFolderAsync(file.LocalPath);
            var parent = parentId > 0 ? parentId.ToString() : _store.Config.CloudFolderId;
            var len = new FileInfo(fullPath).Length;

            if (file.CloudId != null)
            {
                using var s = File.OpenRead(fullPath); await _api.UpdateFileContentAsync(file.CloudId.Value, s, file.FileName);
            }
            else if (len > 10 * 1024 * 1024)
            {
                var uid = await _api.InitChunkUploadAsync(file.FileName, len, parent);
                const int cs = 4 * 1024 * 1024;
                var total = (int)Math.Ceiling((double)len / cs);
                using var stream = File.OpenRead(fullPath);
                var buf = new byte[cs];
                for (int i = 0; i < total; i++)
                {
                    if (!_isRunning) { file.Status = "pending"; FileStatusChanged?.Invoke(file); return; }
                    var r = (int)Math.Min(cs, len - i * cs); if (r < cs) buf = new byte[r];
                    await stream.ReadAsync(buf, 0, r);
                    await _api.UploadChunkAsync(uid, i, buf);
                    file.Progress = (int)(100.0 * (i + 1) / total); FileStatusChanged?.Invoke(file);
                }
                file.CloudId = await _api.CompleteChunkUploadAsync(uid);
            }
            else
            {
                using var s = File.OpenRead(fullPath); var u = await _api.UploadFileAsync(s, file.FileName, parent); file.CloudId = u.Id;
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
            if (File.Exists(fullPath)) { var lh = ComputeHash(fullPath); if (lh != file.LocalHash) file.BackupVersion(_store.Config.LocalPath, "sync"); }
            using var stream = await _api.DownloadFileAsync(file.CloudId.ToString());
            using var fs = File.Create(fullPath); await stream.CopyToAsync(fs);
            file.LocalHash = ComputeHash(fullPath); file.Status = "synced"; file.Progress = 100; FileStatusChanged?.Invoke(file);

            // 下载后也创建本地块存储
            StoreLocalBlocks(fullPath, file.LocalPath);
        }
        catch (Exception ex) { file.Status = "error"; file.Error = ex.Message; FileStatusChanged?.Invoke(file); }
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
                var lp = Path.Combine(_store.Config.LocalPath, file.LocalPath); try { if (File.Exists(lp)) File.Delete(lp); } catch { }
                _store.Files.Remove(file); break;
        }
        SaveState(); StateChanged?.Invoke();
    }
}
