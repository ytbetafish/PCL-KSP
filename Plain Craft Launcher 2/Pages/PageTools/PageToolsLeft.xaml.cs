using System;

namespace PCL;

public partial class PageToolsLeft
{
    public event Action<string>? NavChanged;

    public PageToolsLeft()
    {
        InitializeComponent();
        NavMods.Checked = true;
        NavMods.Check += (s, e) => NavChanged?.Invoke("mods");
        NavCommunity.Check += (s, e) => NavChanged?.Invoke("community");
        NavTools.Check += (s, e) => NavChanged?.Invoke("tools");
    }

    public void SelectNav(string key)
    {
        switch (key)
        {
            case "mods": NavMods.Checked = true; break;
            case "community": NavCommunity.Checked = true; break;
            case "tools": NavTools.Checked = true; break;
        }
    }
}
