using System;
using System.Windows.Controls;
using PCL;
using PCL.Core.App;

namespace PCL.Pages;

public partial class PageAppearance
{
    private bool _loading;

    public PageAppearance()
    {
        InitializeComponent();
        _loading = true;
        SelectComboByTag(CboColorMode, ((int)Config.Preference.Theme.ColorMode).ToString());
        SelectComboByTag(CboColorTheme, ((int)Config.Preference.Theme.LightColor).ToString());
        SliderOpacity.Value = Config.Preference.Theme.WindowOpacity;
        CheckBlur.Checked = Config.Preference.Blur.IsEnabled;
        SliderBlurRadius.Value = Config.Preference.Blur.Radius;
        _loading = false;
    }

    private static void SelectComboByTag(MyComboBox combo, string tag)
    {
        foreach (MyComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void CboColorMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CboColorMode.SelectedItem is not MyComboBoxItem item) return;
        if (!int.TryParse(item.Tag?.ToString(), out var mode)) return;
        Config.Preference.Theme.ColorMode = (ColorMode)mode;
    }

    private void CboColorTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CboColorTheme.SelectedItem is not MyComboBoxItem item) return;
        if (!int.TryParse(item.Tag?.ToString(), out var theme)) return;
        Config.Preference.Theme.DarkColor = (ColorTheme)theme;
        Config.Preference.Theme.LightColor = (ColorTheme)theme;
    }

    private void SliderOpacity_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Preference.Theme.WindowOpacity = SliderOpacity.Value;
    }

    private void CheckBlur_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Preference.Blur.IsEnabled = CheckBlur.Checked == true;
    }

    private void SliderBlurRadius_Change(object sender, bool user)
    {
        if (_loading) return;
        Config.Preference.Blur.Radius = SliderBlurRadius.Value;
    }
}
