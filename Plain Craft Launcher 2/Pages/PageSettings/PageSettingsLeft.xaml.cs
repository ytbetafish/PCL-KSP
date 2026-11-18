using System;
using System.Windows;

namespace PCL;

public partial class PageSettingsLeft
{
    public event Action<string>? NavChanged;

    public PageSettingsLeft()
    {
        InitializeComponent();
        NavAppearance.Checked = true;
        NavCkan.Check += (s, e) => NavChanged?.Invoke("ckan");
        NavTranslate.Check += (s, e) => NavChanged?.Invoke("translate");
        NavLanguage.Check += (s, e) => NavChanged?.Invoke("language");
        NavAppearance.Check += (s, e) => NavChanged?.Invoke("appearance");
        NavMisc.Check += (s, e) => NavChanged?.Invoke("misc");
        NavLog.Check += (s, e) => NavChanged?.Invoke("log");
        NavAbout.Check += (s, e) => NavChanged?.Invoke("about");
    }

    public void SelectNav(string key)
    {
        switch (key)
        {
            case "ckan": NavCkan.Checked = true; break;
            case "translate": NavTranslate.Checked = true; break;
            case "language": NavLanguage.Checked = true; break;
            case "appearance": NavAppearance.Checked = true; break;
            case "misc": NavMisc.Checked = true; break;
            case "log": NavLog.Checked = true; break;
            case "about": NavAbout.Checked = true; break;
        }
    }
}
