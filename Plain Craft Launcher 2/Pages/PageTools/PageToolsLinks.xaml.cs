using System.Windows;
using System.Windows.Input;

namespace PCL;

public partial class PageToolsLinks
{
    public PageToolsLinks()
    {
        InitializeComponent();
    }

    public void ShowGroup(string key)
    {
        PanMods.Visibility = key == "mods" ? Visibility.Visible : Visibility.Collapsed;
        PanCommunity.Visibility = key == "community" ? Visibility.Visible : Visibility.Collapsed;
        PanTools.Visibility = key == "tools" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LinkClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MyListItem item || item.tag is not string url || string.IsNullOrEmpty(url))
            return;
        Common.OpenWebsite(url);
    }
}
