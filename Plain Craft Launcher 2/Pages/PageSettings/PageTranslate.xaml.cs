using System.Windows;
using PCL.Translation;

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
    }

    private void BtnSave_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        KspSettings.Current.TranslateBaseUrl = TxtBaseUrl.Text.Trim();
        KspSettings.Current.TranslateApiKey = TxtApiKey.Text.Trim();
        KspSettings.Current.TranslateModel = TxtModel.Text.Trim();
        KspSettings.Current.HttpProxy = TxtProxy.Text.Trim();
        KspSettings.Save();
        LabSaved.Text = "已保存。";
    }

    private async void BtnTest_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await RunTestAsync();
    }

    /// <summary>
    ///     用一句 KSP 模组简介样例走一遍当前生效的翻译实现，验证接口是否可达。
    /// </summary>
    public async Task RunTestAsync()
    {
        LabTestResult.Text = "翻译中…";
        try
        {
            var svc = TranslationServiceFactory.Create(new TranslationOptions
            {
                BaseUrl = TxtBaseUrl.Text.Trim(),
                ApiKey = TxtApiKey.Text.Trim(),
                Model = TxtModel.Text.Trim()
            });
            var result = await svc.TranslateAsync(
                "This mod adds a suite of science experiments and a new probe core for early-game exploration.", "zh-CN");
            LabTestResult.Text = string.IsNullOrWhiteSpace(result)
                ? "翻译失败：免费接口暂时不可达，或 API 配置有误。"
                : "译文：" + result;
        }
        catch (Exception ex)
        {
            LabTestResult.Text = "翻译失败：" + ex.Message;
        }
    }

}
