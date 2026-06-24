using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace StoraDesktop.Models;

public class FileVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";

    [JsonPropertyName("cloud_version_id")]
    public string? CloudVersionId { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "sync";

    public string SizeDisplay => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{Size / (1024.0 * 1024):F1} MB",
        _ => $"{Size / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public class SyncFileState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("path")]
    public string LocalPath { get; set; } = "";

    [JsonPropertyName("name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("cloud_id")]
    public long? CloudId { get; set; }

    [JsonPropertyName("local_hash")]
    public string LocalHash { get; set; } = "";

    [JsonPropertyName("cloud_hash")]
    public string CloudHash { get; set; } = "";

    [JsonPropertyName("local_mtime")]
    public DateTime LocalModified { get; set; }

    [JsonPropertyName("cloud_mtime")]
    public DateTime CloudModified { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("is_folder")]
    public bool IsFolder { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("versions")]
    public List<FileVersion> Versions { get; set; } = new();

    [JsonPropertyName("current_version")]
    public int CurrentVersion { get; set; } = 1;

    public string StatusIcon => Status switch
    {
        "synced" => "✅",
        "syncing" => "⟳",
        "conflict" => "⚠",
        "rename_conflict" => "🔀",
        "error" => "❌",
        _ => "⏳"
    };

    public string SizeDisplay => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{Size / (1024.0 * 1024):F1} MB",
        _ => $"{Size / (1024.0 * 1024 * 1024):F2} GB"
    };

    public FileVersion BackupVersion(string syncRoot, string reason = "sync")
    {
        CurrentVersion++;
        var ver = new FileVersion
        {
            Version = CurrentVersion,
            Hash = LocalHash,
            Size = Size,
            CreatedAt = DateTime.UtcNow,
            Reason = reason
        };
        Versions.Add(ver);

        // 复制文件到隐藏的版本备份目录
        var fullPath = Path.Combine(syncRoot, LocalPath);
        if (File.Exists(fullPath))
        {
            var storaDir = Path.Combine(syncRoot, ".Stora");
            Directory.CreateDirectory(storaDir);
            if ((File.GetAttributes(storaDir) & FileAttributes.Hidden) != FileAttributes.Hidden)
                File.SetAttributes(storaDir, FileAttributes.Hidden | FileAttributes.Directory);

            Directory.CreateDirectory(Path.Combine(storaDir, "Objects"));
            Directory.CreateDirectory(Path.Combine(storaDir, "versions"));

            var bakDir = Path.Combine(storaDir, "versions");

            var bakName = $"{FileName}.v{ver.Version}.{DateTime.UtcNow:yyyyMMddHHmmss}";
            var bakPath = Path.Combine(bakDir, bakName);
            try { File.Copy(fullPath, bakPath, overwrite: true); ver.LocalPath = bakPath; } catch { }
        }

        while (Versions.Count > 10)
        {
            var old = Versions[0];
            try { if (!string.IsNullOrEmpty(old.LocalPath) && File.Exists(old.LocalPath)) File.Delete(old.LocalPath); } catch { }
            Versions.RemoveAt(0);
        }

        return ver;
    }
}

public class NamingConflict
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";
    [JsonPropertyName("cloud_name")]
    public string CloudName { get; set; } = "";
    [JsonPropertyName("resolved_name")]
    public string? ResolvedName { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("resolved")]
    public bool Resolved { get; set; }
    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }
}

public class SyncConfig
{
    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";
    [JsonPropertyName("cloud_folder_id")]
    public string? CloudFolderId { get; set; }
    [JsonPropertyName("interval_sec")]
    public int IntervalSeconds { get; set; } = 300;
    [JsonPropertyName("conflict_mode")]
    public string ConflictMode { get; set; } = "local";
    [JsonPropertyName("naming_mode")]
    public string NamingMode { get; set; } = "version";
    [JsonPropertyName("keep_versions")]
    public bool KeepVersions { get; set; } = true;
    [JsonPropertyName("max_versions")]
    public int MaxVersions { get; set; } = 10;
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; }
    [JsonPropertyName("whitelist")]
    public List<string> Whitelist { get; set; } = new();
    [JsonPropertyName("blacklist")]
    public List<string> Blacklist { get; set; } = new()
    {
        "*.tmp", "*.temp", "~$*", ".DS_Store", "Thumbs.db"
    };
}

public class SyncStore
{
    [JsonPropertyName("config")]
    public SyncConfig Config { get; set; } = new();
    [JsonPropertyName("files")]
    public List<SyncFileState> Files { get; set; } = new();
    [JsonPropertyName("naming_conflicts")]
    public List<NamingConflict> NamingConflicts { get; set; } = new();
    [JsonPropertyName("last_sync")]
    public DateTime LastSync { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
