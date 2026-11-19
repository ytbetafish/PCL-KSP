using System;
using System.Windows.Controls;
using PCL;
using PCL.Core.App;

namespace PCL.Pages;

public partial class PageAppearance
{
    private bool _loading;
    private bool _homeModeSyncing;

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
        LoadHomeMode();
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

    #region 主页显示方式

    private void LoadHomeMode()
    {
        _homeModeSyncing = true;
        var mode = KspSettings.Current.HomePageMode;
        if (mode == 2 && string.IsNullOrWhiteSpace(KspSettings.Current.HomePageLocalFile))
        {
            mode = 0;
            KspSettings.Current.HomePageMode = 0;
            KspSettings.Save();
        }
        RadioHomeBlank.Checked = mode == 0;
        RadioHomePreset.Checked = mode == 1;
        RadioHomeLocal.Checked = mode == 2;
        TextHomeFile.Text = KspSettings.Current.HomePageLocalFile ?? "";
        PanHomeLocal.Visibility = mode == 2 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PanHomePreset.Visibility = mode == 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        EnsurePresetItems();
        SelectComboByTag(ComboHomePreset, HomePagePreset.Resolve(KspSettings.Current.HomePagePreset).ToString());
        _homeModeSyncing = false;
    }

    private void HomeMode_Check(object sender, Common.RouteEventArgs e)
    {
        if (_homeModeSyncing) return;
        if (sender is not MyRadioBox radio || radio.Checked != true) return;
        if (!int.TryParse(radio.Tag?.ToString(), out var mode)) return;
        _homeModeSyncing = true;
        RadioHomeBlank.Checked = mode == 0;
        RadioHomePreset.Checked = mode == 1;
        RadioHomeLocal.Checked = mode == 2;
        _homeModeSyncing = false;
        KspSettings.Current.HomePageMode = mode;
        KspSettings.Save();
        PanHomeLocal.Visibility = mode == 2 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PanHomePreset.Visibility = mode == 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void BtnHomeBrowse_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XAML 主页文件|*.xaml|所有文件|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        TextHomeFile.Text = dlg.FileName;
        KspSettings.Current.HomePageLocalFile = dlg.FileName;
        if (KspSettings.Current.HomePageMode != 2)
        {
            _homeModeSyncing = true;
            RadioHomeBlank.Checked = false;
            RadioHomePreset.Checked = false;
            RadioHomeLocal.Checked = true;
            _homeModeSyncing = false;
            KspSettings.Current.HomePageMode = 2;
            PanHomeLocal.Visibility = System.Windows.Visibility.Visible;
            PanHomePreset.Visibility = System.Windows.Visibility.Collapsed;
        }
        KspSettings.Save();
    }



    private void EnsurePresetItems()
    {
        if (ComboHomePreset.Items.Count == HomePagePreset.All.Length && ComboHomePreset.Items.Count > 0) return;
        ComboHomePreset.Items.Clear();
        foreach (var preset in HomePagePreset.All)
            ComboHomePreset.Items.Add(new MyComboBoxItem { Content = preset.Title, Tag = preset.Id.ToString() });
    }

    private void ComboHomePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_homeModeSyncing) return;
        if (ComboHomePreset.SelectedItem is not MyComboBoxItem item) return;
        if (!int.TryParse(item.Tag?.ToString(), out var preset)) return;
        KspSettings.Current.HomePagePreset = preset;
        KspSettings.Save();
    }
    
    #endregion
}
