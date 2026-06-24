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
}

// API 分页响应 — 匹配实际返回格式
// API 外层响应包装
public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

// API 文件列表数据
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