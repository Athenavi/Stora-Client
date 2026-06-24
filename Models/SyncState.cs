using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StoraDesktop.Models;

/// <summary>
/// 单个文件的同步状态
/// </summary>
public class SyncFileState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("path")]
    public string LocalPath { get; set; } = "";       // 本地相对路径（相对于同步根目录）

    [JsonPropertyName("name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("cloud_id")]
    public long? CloudId { get; set; }                 // Stora 云端文件 ID

    [JsonPropertyName("local_hash")]
    public string LocalHash { get; set; } = "";        // 本地 SHA256

    [JsonPropertyName("cloud_hash")]
    public string CloudHash { get; set; } = "";        // 云端 file_hash

    [JsonPropertyName("local_mtime")]
    public DateTime LocalModified { get; set; }

    [JsonPropertyName("cloud_mtime")]
    public DateTime CloudModified { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";    // pending/synced/syncing/conflict/error

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";        // upload/download

    [JsonPropertyName("progress")]
    public int Progress { get; set; }                  // 0-100

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("is_folder")]
    public bool IsFolder { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public string StatusIcon => Status switch
    {
        "synced" => "✅",
        "syncing" => "⟳",
        "conflict" => "⚠",
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
}

/// <summary>
/// 同步盘配置
/// </summary>
public class SyncConfig
{
    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";

    [JsonPropertyName("cloud_folder_id")]
    public string? CloudFolderId { get; set; }         // Stora 云端目标文件夹 ID

    [JsonPropertyName("interval_sec")]
    public int IntervalSeconds { get; set; } = 300;    // 默认 5 分钟

    [JsonPropertyName("conflict_mode")]
    public string ConflictMode { get; set; } = "local"; // local / cloud / manual

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; }

    [JsonPropertyName("whitelist")]
    public List<string> Whitelist { get; set; } = new(); // 空=全部

    [JsonPropertyName("blacklist")]
    public List<string> Blacklist { get; set; } = new()
    {
        "*.tmp", "*.temp", "~$*", ".DS_Store", "Thumbs.db"
    };
}

/// <summary>
/// 完整的同步状态（持久化到 JSON）
/// </summary>
public class SyncStore
{
    [JsonPropertyName("config")]
    public SyncConfig Config { get; set; } = new();

    [JsonPropertyName("files")]
    public List<SyncFileState> Files { get; set; } = new();

    [JsonPropertyName("last_sync")]
    public DateTime LastSync { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
