using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Controls;
using PCL;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL.Pages;

public partial class PageLanguage
{
    private bool _loading;

    public PageLanguage()
    {
        InitializeComponent();
        ReloadLanguageCombo();
        ReloadFormatCultureCombo();
    }

    private void ReloadLanguageCombo()
    {
        _loading = true;
        CboUiLanguage.Items.Clear();
        CboUiLanguage.Items.Add(new MyComboBoxItem { Content = "自动（跟随系统）", Tag = LocalizationService.Auto });
        foreach (var language in LocalizationService.SupportedLanguages)
            CboUiLanguage.Items.Add(new MyComboBoxItem { Content = language.NativeName, Tag = language.Code });
        SelectComboByTag(CboUiLanguage,
            string.IsNullOrWhiteSpace(Config.Preference.Localization.Language)
                ? LocalizationService.Auto
                : Config.Preference.Localization.Language);
        _loading = false;
    }

    private void ReloadFormatCultureCombo()
    {
        _loading = true;
        CboFormatCulture.Items.Clear();
        CboFormatCulture.Items.Add(new MyComboBoxItem { Content = "自动（跟随系统）", Tag = LocalizationService.Auto });
        CboFormatCulture.Items.Add(new MyComboBoxItem { Content = "跟随界面语言", Tag = LocalizationService.FormatCultureFollowLanguage });
        foreach (var culture in GetBuiltInFormatCultures())
            CboFormatCulture.Items.Add(new MyComboBoxItem { Content = culture.NativeName, Tag = culture.Name });
        SelectComboByTag(CboFormatCulture,
            string.IsNullOrWhiteSpace(Config.Preference.Localization.FormatCulture)
                ? LocalizationService.Auto
                : Config.Preference.Localization.FormatCulture);
        _loading = false;
    }

    private static IEnumerable<CultureInfo> GetBuiltInFormatCultures()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var culture = CultureInfo.GetCultureInfo(language.CultureName);
            if (used.Add(culture.Name)) yield return culture;
        }
    }

    private static void SelectComboByTag(MyComboBox combo, string tag)
    {
        foreach (MyComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void CboUiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CboUiLanguage.SelectedItem is not MyComboBoxItem item) return;
        var value = item.Tag?.ToString() ?? LocalizationService.Auto;
        if (Config.Preference.Localization.Language == value) return;
        Config.Preference.Localization.Language = value;
        LocalizationService.ApplyFromConfig(save: true);
    }

    private void CboFormatCulture_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CboFormatCulture.SelectedItem is not MyComboBoxItem item) return;
        var value = item.Tag?.ToString() ?? LocalizationService.Auto;
        if (Config.Preference.Localization.FormatCulture == value) return;
        Config.Preference.Localization.FormatCulture = value;
        LocalizationService.ApplyFromConfig(save: true);
    }
}
