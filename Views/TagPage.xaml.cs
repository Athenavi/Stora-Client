using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoraDesktop.Models;
using StoraDesktop.ViewModels;

namespace StoraDesktop.Views;

public sealed partial class TagPage : Page
{
    public TagViewModel VM { get; }

    public TagPage(TagViewModel vm)
    {
        this.InitializeComponent();
        VM = vm;
        Loaded += async (s, e) => await VM.LoadAsync();
    }

    private async void OnRightTap(object s, RightTappedRoutedEventArgs e)
    {
        var tag = (e.OriginalSource as FrameworkElement)?.DataContext as TagItem;
        if (tag == null) return;

        var menu = new MenuFlyout();
        var delItem = new MenuFlyoutItem { Text = "🗑 删除标签" };
        delItem.Click += async (s2, e2) => { await VM.DeleteTagCommand.ExecuteAsync(tag); };
        menu.Items.Add(delItem);
        menu.ShowAt(TagList, e.GetPosition(TagList));
    }
}
