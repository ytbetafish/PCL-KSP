using System.Windows;

namespace PCL.Pages;

public partial class PageTranslate
{
    public PageTranslate()
    {
        InitializeComponent();
        TxtBaseUrl.Text = KspSettings.Current.TranslateBaseUrl;
        TxtApiKey.Text = KspSettings.Current.TranslateApiKey;
        TxtModel.Text = KspSettings.Current.TranslateModel;
        TxtProxy.Text = KspSettings.Current.HttpProxy;
        TxtConcurrency.Text = KspSettings.Current.DownloadConcurrency.ToString();
        TxtTimeout.Text = KspSettings.Current.HttpTimeoutSeconds.ToString();
    }

    private void BtnSave_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        KspSettings.Current.TranslateBaseUrl = TxtBaseUrl.Text.Trim();
        KspSettings.Current.TranslateApiKey = TxtApiKey.Text.Trim();
        KspSettings.Current.TranslateModel = TxtModel.Text.Trim();
        KspSettings.Current.HttpProxy = TxtProxy.Text.Trim();
        int.TryParse(TxtConcurrency.Text.Trim(), out var conc);
        if (conc > 0) KspSettings.Current.DownloadConcurrency = conc;
        int.TryParse(TxtTimeout.Text.Trim(), out var to);
        if (to > 0) KspSettings.Current.HttpTimeoutSeconds = to;
        KspSettings.Save();
        LabSaved.Text = "已保存。";
    }
}
