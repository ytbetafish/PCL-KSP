using System;

namespace PCL;

public partial class PageToolsLeft
{
    public event Action<string>? NavChanged;

    public PageToolsLeft()
    {
        InitializeComponent();
        NavDownload.Checked = true;
        NavDownload.Check += (s, e) => NavChanged?.Invoke("download");
        NavGuide.Check += (s, e) => NavChanged?.Invoke("guide");
        NavCommunity.Check += (s, e) => NavChanged?.Invoke("community");
        NavTools.Check += (s, e) => NavChanged?.Invoke("tools");
    }

    public void SelectNav(string key)
    {
        switch (key)
        {
            case "download": NavDownload.Checked = true; break;
            case "guide": NavGuide.Checked = true; break;
            case "community": NavCommunity.Checked = true; break;
            case "tools": NavTools.Checked = true; break;
            default: NavDownload.Checked = true; break;
        }
    }
}
