using StoraDesktop.Models;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System;
namespace StoraDesktop.Services;

public class StoraApiClient
{
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private string? _refreshToken;

    // 服务器地址，可在设置中更改
    public string BaseUrl { get; set; } = "http://localhost:9421";
    public bool IsAuthenticated => _accessToken != null;

    public StoraApiClient()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    #region 认证

    /// <summary>
    /// 🔐 设置访问令牌
    /// </summary>
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

    /// <summary>
    /// 🔑 登录
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password)
        });

        var response = await _httpClient.PostAsync($"{BaseUrl}/api/v2/auth/login", form);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result != null)
        {
            SetTokens(result.AccessToken, result.RefreshToken);
        }
        return result ?? throw new Exception("登录失败：响应为空");
    }

    /// <summary>
    /// 🔄 刷新令牌
    /// </summary>
    public async Task<LoginResponse?> RefreshTokenAsync()
    {
        if (_refreshToken == null) return null;

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("refresh_token", _refreshToken)
        });

        var response = await _httpClient.PostAsync($"{BaseUrl}/api/v2/auth/refresh", form);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result != null)
        {
            SetTokens(result.AccessToken, result.RefreshToken);
        }
        return result;
    }

    /// <summary>
    /// 🚪 登出
    /// </summary>
    public async Task LogoutAsync()
    {
        try
        {
            await _httpClient.PostAsync($"{BaseUrl}/api/v2/auth/logout", null);
        }
        finally
        {
            ClearTokens();
        }
    }

    /// <summary>
    /// 👤 获取当前用户信息
    /// </summary>
    public async Task<UserInfo> GetCurrentUserAsync()
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/v2/auth/me");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserInfo>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new Exception("获取用户信息失败");
    }

    #endregion

    #region 文件操作

    /// <summary>
    /// 📂 获取文件列表
    /// </summary>
    public async Task<FileListData> GetFilesAsync(
    string? folderId = null,
    int page = 1,
    int perPage = 50)
    {
        var url = $"{BaseUrl}/api/v2/files?page={page}&per_page={perPage}";
        if (!string.IsNullOrEmpty(folderId))
            url += $"&folder_id={folderId}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<FileListData>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return wrapper?.Data ?? new FileListData();
    }

    /// <summary>
    /// ⬆️ 上传文件
    /// </summary>
    public async Task<FileItem> UploadFileAsync(Stream fileStream, string fileName, string? folderId = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        if (!string.IsNullOrEmpty(folderId))
            content.Add(new StringContent(folderId), "folder_id");

        var response = await _httpClient.PostAsync($"{BaseUrl}/api/v2/files/upload", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FileItem>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new Exception("上传失败");
    }

    /// <summary>
    /// ⬇️ 下载文件
    /// </summary>
    public async Task<Stream> DownloadFileAsync(string fileId)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/v2/files/{fileId}/download");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// 🗑️ 删除文件
    /// </summary>
    public async Task DeleteFileAsync(string fileId)
    {
        var response = await _httpClient.DeleteAsync($"{BaseUrl}/api/v2/files/{fileId}");
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region 文件夹操作

    /// <summary>
    /// 📁 获取文件夹树
    /// </summary>
    public async Task<List<FileItem>> GetFolderTreeAsync()
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/v2/files/folders/tree");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<FileItem>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new List<FileItem>();
    }

    /// <summary>
    /// 📁 创建文件夹
    /// </summary>
    public async Task<FileItem> CreateFolderAsync(string name, string? parentId = null)
    {
        var body = new { name, parent_id = parentId };
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/v2/files/folders", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FileItem>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new Exception("创建文件夹失败");
    }

    #endregion
}