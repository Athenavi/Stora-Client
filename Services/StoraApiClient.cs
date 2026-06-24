using StoraDesktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace StoraDesktop.Services;

public class StoraApiClient
{
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private string? _refreshToken;

    public string BaseUrl { get; set; } = "http://localhost:9421";
    public bool IsAuthenticated => _accessToken != null;

    public StoraApiClient()
    {
        _httpClient = new HttpClient();
    }

    private JsonSerializerOptions JsonOpts => new() { PropertyNameCaseInsensitive = true };

    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        var r = await _httpClient.GetAsync($"{BaseUrl}{path}");
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
        return w?.Data;
    }

    private async Task<List<T>> GetListAsync<T>(string path) where T : class, new()
    {
        var r = await _httpClient.GetAsync($"{BaseUrl}{path}");
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<List<T>>>(JsonOpts);
        return w?.Data ?? new List<T>();
    }

    #region Auth

    public void SetTokens(string accessToken, string refreshToken)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public void ClearTokens()
    {
        _accessToken = null;
        _refreshToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var form = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password)
        });
        var response = await _httpClient.PostAsync($"{BaseUrl}/api/v2/auth/login", form);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        if (result != null) SetTokens(result.AccessToken, result.RefreshToken);
        return result ?? throw new Exception("登录失败");
    }

    public async Task<LoginResponse?> RefreshTokenAsync()
    {
        if (_refreshToken == null) return null;
        var form = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("refresh_token", _refreshToken)
        });
        var response = await _httpClient.PostAsync($"{BaseUrl}/api/v2/auth/refresh", form);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        if (result != null) SetTokens(result.AccessToken, result.RefreshToken);
        return result;
    }

    public async Task LogoutAsync()
    {
        try { await _httpClient.PostAsync($"{BaseUrl}/api/v2/auth/logout", null); }
        finally { ClearTokens(); }
    }

    public async Task<UserInfo> GetCurrentUserAsync()
    {
        var r = await _httpClient.GetAsync($"{BaseUrl}/api/v2/auth/me");
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<UserInfo>>(JsonOpts);
        return w?.Data ?? throw new Exception("获取用户信息失败");
    }

    #endregion

    #region Files
    /// <summary>
    /// 📁 按路径创建文件夹（不存在则创建）
    /// </summary>
    public async Task<FileItem> CreateFolderByPathAsync(string path)
    {
        var segs = path.Trim(new char[] { '/' }).Split('/');
        var name = segs[segs.Length - 1];
        var parentPath = segs.Length > 1 ? string.Join("/", segs[0..^1]) : "";
        var body = new { name, parent_path = parentPath };
        var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/folders/by-path", body, JsonOpts);
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<FileItem>>(JsonOpts);
        return w?.Data ?? throw new Exception("创建文件夹失败");
    }

    /// <summary>
    /// 🔄 更新文件内容（触发后端版本创建）
    /// </summary>
    public async Task UpdateFileContentAsync(long fileId, Stream fileStream, string fileName, string mimeType = "application/octet-stream")
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "content", fileName);
        if (!string.IsNullOrEmpty(mimeType)) content.Add(new StringContent(mimeType), "mime_type");
        var r = await _httpClient.PutAsync($"{BaseUrl}/api/v2/files/{fileId}/content", content);
        r.EnsureSuccessStatusCode();
    }


    public async Task<FileListData> GetFilesAsync(string? folderId = null, int page = 1, int perPage = 50, string? fileType = null, bool? isFavorite = null)
    {
        var url = $"{BaseUrl}/api/v2/files?page={page}&per_page={perPage}";
        if (!string.IsNullOrEmpty(folderId)) url += $"&folder_id={folderId}";
        if (!string.IsNullOrEmpty(fileType)) url += $"&file_type={fileType}";
        if (isFavorite == true) url += "&is_favorite=true";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<FileListData>>(JsonOpts);
        return wrapper?.Data ?? new FileListData();
    }

    public async Task<FileItem> UploadFileAsync(Stream fileStream, string fileName, string? folderId = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        if (!string.IsNullOrEmpty(folderId)) content.Add(new StringContent(folderId), "folder_id");
        var response = await _httpClient.PostAsync($"{BaseUrl}/api/v2/files/upload", content);
        response.EnsureSuccessStatusCode();
        var w = await response.Content.ReadFromJsonAsync<ApiResponse<FileItem>>(JsonOpts);
        return w?.Data ?? throw new Exception("上传失败");
    }

    public async Task<string> InitChunkUploadAsync(string fileName, long fileSize, string? folderId = null)
    {
        var body = new { filename = fileName, file_size = fileSize, folder_id = folderId };
        var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/upload/init", body, JsonOpts);
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<Dictionary<string, string>>>(JsonOpts);
        return w?.Data?.GetValueOrDefault("upload_id") ?? throw new Exception("初始化上传失败");
    }

    public async Task UploadChunkAsync(string uploadId, int index, byte[] data)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(data), "chunk", $"chunk_{index}");
        content.Add(new StringContent(index.ToString()), "index");
        var r = await _httpClient.PostAsync($"{BaseUrl}/api/v2/files/upload/chunk?upload_id={uploadId}", content);
        r.EnsureSuccessStatusCode();
    }

    public async Task CompleteChunkUploadAsync(string uploadId)
    {
        var r = await _httpClient.PostAsync($"{BaseUrl}/api/v2/files/upload/{uploadId}/complete", null);
        r.EnsureSuccessStatusCode();
    }

    public async Task<Stream> DownloadFileAsync(string fileId)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/v2/files/{fileId}/download");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    public async Task<FileItem> CreateFolderAsync(string name, string? parentId = null)
    {
        object? pid = null;
        if (long.TryParse(parentId, out var n)) pid = n;
        var body = new { name, parent_id = pid };
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/folders", body, JsonOpts);
        response.EnsureSuccessStatusCode();
        var w = await response.Content.ReadFromJsonAsync<ApiResponse<FileItem>>(JsonOpts);
        return w?.Data ?? throw new Exception("创建文件夹失败");
    }
    public async Task DeleteFileAsync(string fileId)
    {
        var response = await _httpClient.DeleteAsync($"{BaseUrl}/api/v2/files/{fileId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task ToggleFavoriteAsync(string fileId)
    {
        var r = await _httpClient.PutAsync($"{BaseUrl}/api/v2/files/{fileId}/favorite", null);
        r.EnsureSuccessStatusCode();
    }

    #endregion

    #region Shares

    public async Task<List<ShareItem>> GetSharesAsync()
    {
        return await GetListAsync<ShareItem>("/api/v2/files/shares");
    }

    public async Task<ShareItem> CreateShareAsync(long fileId, string permission = "view", string? expireAt = null)
    {
        var body = new { file_id = fileId, permission, expire_at = expireAt };
        var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/shares", body, JsonOpts);
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<ShareItem>>(JsonOpts);
        return w?.Data ?? throw new Exception("创建分享失败");
    }

    public async Task DeleteShareAsync(string shareId)
    {
        var r = await _httpClient.DeleteAsync($"{BaseUrl}/api/v2/files/shares/{shareId}");
        r.EnsureSuccessStatusCode();
    }

    #endregion

    #region Vault

    public async Task<List<VaultItem>> GetVaultsAsync()
    {
        return await GetListAsync<VaultItem>("/api/v2/vaults");
    }

    public async Task<VaultItem> CreateVaultAsync(string name, string password)
    {
        var body = new { name, password };
        var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/vaults", body, JsonOpts);
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<VaultItem>>(JsonOpts);
        return w?.Data ?? throw new Exception("创建保险箱失败");
    }

    public async Task<string> VerifyVaultPasswordAsync(long vaultId, string password)
    {
        var body = new { password };
        using var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/vaults/{vaultId}/verify-password", body, JsonOpts);
        r.EnsureSuccessStatusCode();
        // 后端直接返回 {"token":"...", "vault_id":...}
        var dict = await r.Content.ReadFromJsonAsync<Dictionary<string, string>>(JsonOpts);
        var token = dict?.GetValueOrDefault("token") ?? dict?.GetValueOrDefault("vault_token");
        return token ?? throw new Exception("密码错误");
    }

    public async Task<List<VaultFileItem>> GetVaultItemsAsync(long vaultId, string vaultToken)
    {
        var r = await _httpClient.GetAsync($"{BaseUrl}/api/v2/vaults/{vaultId}/items?vault_token={vaultToken}");
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<List<VaultFileItem>>>(JsonOpts);
        return w?.Data ?? new List<VaultFileItem>();
    }

    public async Task DeleteVaultAsync(long vaultId)
    {
        var r = await _httpClient.DeleteAsync($"{BaseUrl}/api/v2/vaults/{vaultId}");
        r.EnsureSuccessStatusCode();
    }

    #endregion

    #region Tags

    public async Task<List<TagItem>> GetTagsAsync()
    {
        return await GetListAsync<TagItem>("/api/v2/files/tags");
    }

    public async Task<TagItem> CreateTagAsync(string name, string? color = null)
    {
        var body = new { name, color };
        var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/tags", body, JsonOpts);
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<TagItem>>(JsonOpts);
        return w?.Data ?? throw new Exception("创建标签失败");
    }

    public async Task DeleteTagAsync(string tagId)
    {
        var r = await _httpClient.DeleteAsync($"{BaseUrl}/api/v2/files/tags/{tagId}");
        r.EnsureSuccessStatusCode();
    }

    public async Task AssignTagAsync(long fileId, long tagId)
    {
        var r = await _httpClient.PostAsync($"{BaseUrl}/api/v2/files/{fileId}/tags?tag_id={tagId}", null);
        r.EnsureSuccessStatusCode();
    }

    #endregion

    #region Offline Download

    public async Task CreateOfflineDownloadAsync(string url)
    {
        var body = new { url };
        var r = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/offline-download", body, JsonOpts);
        r.EnsureSuccessStatusCode();
    }

    public async Task<List<OfflineDownloadItem>> GetOfflineDownloadsAsync()
    {
        var r = await _httpClient.GetAsync($"{BaseUrl}/api/v2/files/offline-download");
        r.EnsureSuccessStatusCode();
        var w = await r.Content.ReadFromJsonAsync<ApiResponse<List<OfflineDownloadItem>>>(JsonOpts);
        return w?.Data ?? new List<OfflineDownloadItem>();
    }

    #endregion

    #region Versions

    public async Task<List<FileVersionInfo>> GetVersionsAsync(long fileId)
    {
        var r = await _httpClient.GetAsync($"{BaseUrl}/api/v2/files/{fileId}/versions");
        r.EnsureSuccessStatusCode();
        var list = await r.Content.ReadFromJsonAsync<List<FileVersionInfo>>(JsonOpts);
        return list ?? new List<FileVersionInfo>();
    }

    public async Task RestoreVersionAsync(long fileId, long versionId)
    {
        var r = await _httpClient.PostAsync($"{BaseUrl}/api/v2/files/{fileId}/versions/{versionId}/restore", null);
        r.EnsureSuccessStatusCode();
    }

    #endregion
}






