using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StoraDesktop.Models;

public class FileItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("filename")]
    public string? Name { get; set; } = string.Empty;

    [JsonPropertyName("file_size")]
    public long? Size { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("folder_id")]
    public long? FolderId { get; set; }

    [JsonPropertyName("is_folder")]
    public bool IsFolder { get; set; }

    [JsonPropertyName("is_favorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("original_filename")]
    public string? OriginalFilename { get; set; }

    [JsonPropertyName("file_type")]
    public string? FileType { get; set; }

    public string SizeDisplay => (Size ?? 0) switch
    {
        < 1024 => $"{Size ?? 0} B",
        < 1024 * 1024 => $"{((Size ?? 0) / 1024.0):F1} KB",
        < 1024 * 1024 * 1024 => $"{((Size ?? 0) / (1024.0 * 1024)):F1} MB",
        _ => $"{((Size ?? 0) / (1024.0 * 1024 * 1024)):F2} GB"
    };

    public string Icon => IsFolder ? "📁" : "📄";
    public string CategoryIcon => FileType switch
    {
        "image" => "🖼",
        "video" => "🎬",
        "audio" => "🎵",
        "document" => "📄",
        "archive" => "📦",
        _ => "📄"
    };
}

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class FileListData
{
    [JsonPropertyName("items")]
    public List<FileItem> Items { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }
}

// ===== 分享模型 =====
public class ShareItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("file_id")]
    public long? FileId { get; set; }

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("is_folder")]
    public bool IsFolder { get; set; }

    [JsonPropertyName("permission")]
    public string Permission { get; set; } = "view";

    [JsonPropertyName("expire_at")]
    public string? ExpireAt { get; set; }

    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    public string ShareUrl => $"https://stora.app/share/{Code}";
}

// ===== 保险箱模型 =====
public class VaultItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }
}

public class VaultFileItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("filename")]
    public string Name { get; set; } = "";

    [JsonPropertyName("file_size")]
    public long? Size { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    public string SizeDisplay => (Size ?? 0) switch
    {
        < 1024 => $"{Size ?? 0} B",
        < 1024 * 1024 => $"{((Size ?? 0) / 1024.0):F1} KB",
        < 1024 * 1024 * 1024 => $"{((Size ?? 0) / (1024.0 * 1024)):F1} MB",
        _ => $"{((Size ?? 0) / (1024.0 * 1024 * 1024)):F2} GB"
    };
}

// ===== 标签模型 =====
public class TagItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }
}

// ===== 离线下载模型 =====
public class OfflineDownloadItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";
}

// ===== 版本模型 =====
public class FileVersionInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("version_num")]
    public int VersionNum { get; set; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("file_hash")]
    public string? FileHash { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    public string SizeDisplay => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{FileSize / (1024.0 * 1024):F1} MB",
        _ => $"{FileSize / (1024.0 * 1024 * 1024):F2} GB"
    };
}
