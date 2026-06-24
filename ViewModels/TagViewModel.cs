using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoraDesktop.Models;
using StoraDesktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace StoraDesktop.ViewModels;

public partial class TagViewModel : ObservableObject
{
    private readonly StoraApiClient _api;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _newTagName = "";

    public ObservableCollection<TagItem> Tags { get; } = new();

    public TagViewModel(StoraApiClient api) { _api = api; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true; _status = "加载中...";
            var items = await _api.GetTagsAsync();
            Tags.Clear();
            foreach (var t in items) Tags.Add(t);
            _status = $"共 {items.Count} 个标签";
        }
        catch (Exception ex) { _status = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateTagAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTagName)) return;
        try
        {
            var tag = await _api.CreateTagAsync(_newTagName);
            Tags.Add(tag);
            _newTagName = "";
            _status = $"标签 \"{tag.Name}\" 已创建";
        }
        catch (Exception ex) { _status = $"创建失败: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task DeleteTagAsync(TagItem? t)
    {
        if (t == null) return;
        try
        {
            await _api.DeleteTagAsync(t.Id.ToString());
            Tags.Remove(t);
            _status = $"标签 \"{t.Name}\" 已删除";
        }
        catch (Exception ex) { _status = $"删除失败: {ex.Message}"; }
    }
}
