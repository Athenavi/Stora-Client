using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace StoraDesktop.Services;

/// <summary>
/// .Stora/index.db — SQLite 文件生命周期管理
/// 替代 JSON 存储，支持快照链、块索引、低 I/O 写入
/// </summary>
public class StoraIndex : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly string _storaPath;
    private readonly object _lock = new();

    public string StoraPath => _storaPath;

    public StoraIndex(string syncRoot)
    {
        _storaPath = Path.Combine(syncRoot, ".Stora");
        Directory.CreateDirectory(Path.Combine(_storaPath, "Objects"));
        Directory.CreateDirectory(Path.Combine(_storaPath, "versions"));
        Directory.CreateDirectory(Path.Combine(_storaPath, "manifests"));

        // 隐藏目录
        var di = new DirectoryInfo(_storaPath);
        if ((di.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
            di.Attributes |= FileAttributes.Hidden;

        var dbPath = Path.Combine(_storaPath, "index.db");
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS files (
                path TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                mtime TEXT,
                size INTEGER DEFAULT 0,
                current_hash TEXT,
                last_synced_hash TEXT,
                cloud_id INTEGER,
                status TEXT DEFAULT 'pending'
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                parent_id INTEGER REFERENCES snapshots(id),
                tree_hash TEXT,
                message TEXT,
                file_count INTEGER DEFAULT 0,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS snapshot_files (
                snapshot_id INTEGER NOT NULL REFERENCES snapshots(id),
                file_path TEXT NOT NULL,
                file_hash TEXT,
                action TEXT NOT NULL,
                size INTEGER DEFAULT 0,
                PRIMARY KEY (snapshot_id, file_path)
            );

            CREATE TABLE IF NOT EXISTS blocks (
                hash TEXT PRIMARY KEY,
                size INTEGER DEFAULT 0,
                ref_count INTEGER DEFAULT 1,
                storage_path TEXT
            );
        ";
        cmd.ExecuteNonQuery();
    }

    // ── 文件记录 ──

    public void UpsertFile(string path, string name, string hash, long size, string mtime)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO files (path, name, current_hash, size, mtime, status)
                VALUES ($p, $n, $h, $s, $m, 'pending')
                ON CONFLICT(path) DO UPDATE SET
                    current_hash = $h, size = $s, mtime = $m,
                    status = CASE WHEN status = 'synced' THEN 'pending' ELSE status END";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$s", size);
            cmd.Parameters.AddWithValue("$m", mtime);
            cmd.ExecuteNonQuery();
        }
    }

    public void MarkSynced(string path, long cloudId, string hash)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE files SET status = 'synced', last_synced_hash = $h, cloud_id = $c WHERE path = $p";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$c", cloudId);
            cmd.ExecuteNonQuery();
        }
    }

    public void RemoveFile(string path)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE path = $p";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }
    }

    public long? GetCloudId(string path)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT cloud_id FROM files WHERE path = $p";
            cmd.Parameters.AddWithValue("$p", path);
            var v = cmd.ExecuteScalar();
            return v is not null && v != DBNull.Value ? (long?)Convert.ToInt64(v) : null;
        }
    }

    public string? GetHash(string path)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT current_hash FROM files WHERE path = $p";
            cmd.Parameters.AddWithValue("$p", path);
            return cmd.ExecuteScalar() as string;
        }
    }

    public List<(string path, string hash, long cloudId)> GetPendingFiles()
    {
        lock (_lock)
        {
            var list = new List<(string, string, long)>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT path, current_hash, COALESCE(cloud_id, 0) FROM files WHERE status = 'pending'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
            return list;
        }
    }

    public List<(string path, string hash, long cloudId)> GetAllFiles()
    {
        lock (_lock)
        {
            var list = new List<(string, string, long)>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT path, COALESCE(current_hash,''), COALESCE(cloud_id,0) FROM files";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
            return list;
        }
    }

    // ── 快照（Git 式提交） ──

    public long CreateSnapshot(string message, List<(string path, string hash, string action)> changes)
    {
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();

            // 获取最后 snapshot id
            long? parentId = null;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT MAX(id) FROM snapshots";
                var v = cmd.ExecuteScalar();
                if (v != DBNull.Value && v != null) parentId = Convert.ToInt64(v);
            }

            // 创建 snapshot
            long snapId;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO snapshots (parent_id, message, file_count, created_at)
                    VALUES ($p, $m, $c, datetime('now')); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$m", message);
                cmd.Parameters.AddWithValue("$c", changes.Count);
                snapId = (long)cmd.ExecuteScalar();
            }

            // 写入变更文件
            foreach (var (path, hash, action) in changes)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = @"INSERT INTO snapshot_files (snapshot_id, file_path, file_hash, action)
                    VALUES ($s, $p, $h, $a)";
                cmd.Parameters.AddWithValue("$s", snapId);
                cmd.Parameters.AddWithValue("$p", path);
                cmd.Parameters.AddWithValue("$h", hash ?? "");
                cmd.Parameters.AddWithValue("$a", action);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return snapId;
        }
    }

    public int GetSnapshotCount() { lock (_lock) { using var c = _db.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM snapshots"; return Convert.ToInt32(c.ExecuteScalar()); } }

    // ── 块管理 ──

    public void StoreBlock(string hash, byte[] data)
    {
        var subDir = Path.Combine(_storaPath, "Objects", hash.Substring(0, 2));
        Directory.CreateDirectory(subDir);
        var path = Path.Combine(subDir, hash.Substring(2));
        if (!File.Exists(path)) File.WriteAllBytes(path, data);

        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"INSERT INTO blocks (hash, size, ref_count, storage_path) VALUES ($h, $s, 1, $p)
                ON CONFLICT(hash) DO UPDATE SET ref_count = ref_count + 1";
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$s", data.Length);
            cmd.Parameters.AddWithValue("$p", $"Objects/{hash.Substring(0, 2)}/{hash.Substring(2)}");
            cmd.ExecuteNonQuery();
        }
    }

    // ── Journal 追加 ──

    public void AppendJournal(string path, string action, string hash = "", long size = 0)
    {
        try
        {
            var jp = Path.Combine(_storaPath, "journal.jsonl");
            var entry = System.Text.Json.JsonSerializer.Serialize(new
            {
                time = DateTime.UtcNow.ToString("O"),
                path, action, hash, size
            });
            File.AppendAllText(jp, entry + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose() { _db?.Close(); _db?.Dispose(); }
}
