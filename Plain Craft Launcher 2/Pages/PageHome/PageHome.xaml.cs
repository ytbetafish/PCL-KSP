using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace PCL.Pages;

public partial class PageHome
{
    private const string UpdateLogText =
        "2026-11-19\n" +
        "· 主页按原版结构恢复：左栏启动面板（含启动中动画、进度、阶段与取消），右栏公告 / 社区 / 更新日志卡片。\n" +
        "· 设置页「个性化 → 主页」可选择主页显示方式：空白、预设、本地 XAML 文件。\n" +
        "· 切页进入动画补充透明度兜底，修复控件需要悬停才显示的问题。\n" +
        "· 启动页显示 KSP 版本与安装路径，启动失败给出明确提示。\n" +
        "· 历史提交清理本机路径与误提交的构建产物，仓库更名为 PCL-KSP。";

    public PageHome()
    {
        InitializeComponent();
        ApplyHomeMode();
    }

    private void ApplyHomeMode()
    {
        var mode = KspSettings.Current.HomePageMode;
        if (mode == 1)
        {
            PanPreset.Visibility = Visibility.Visible;
            LoadPreset();
            return;
        }
        PanPreset.Visibility = Visibility.Collapsed;
        if (mode == 2)
        {
            LoadCustom(KspSettings.Current.HomePageLocalFile);
        }
    }

    private void LoadPreset()
    {
        LabWelcome.Text = "PCL KSP 修改版 v" + PCL.Core.App.Basics.VersionName;
        LabWelcomeSub.Text = "基于 PCL-CE 改造的《坎巴拉太空计划》启动器，使用 CKAN 管理模组。";
        LabWelcomeNotice.Text =
            "当前为开发中版本，功能仍在补齐。启动前请先在设置里确认 KSP 安装路径；" +
            "模组安装与更新走 CKAN，公共索引默认使用坎喵加速镜像。";
        LabLog.Text = UpdateLogText;
    }

    private void LoadCustom(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError("尚未选择本地主页文件。");
            return;
        }
        if (!File.Exists(path))
        {
            ShowError("找不到主页文件：" + path);
            return;
        }
        try
        {
            var text = File.ReadAllText(path);
            var element = XamlReader.Parse(text) as UIElement;
            if (element is null)
            {
                ShowError("该文件不是可显示的 XAML 页面：" + path);
                return;
            }
            if (element is FrameworkElement frameworkElement)
                frameworkElement.Margin = new Thickness(0);
            PanCustomHost.Content = element;
            PanCustomHost.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError("主页文件解析失败：" + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        LabCustomError.Text = message;
        CardCustomError.Visibility = Visibility.Visible;
    }

    private void BtnCommunityClose_Click(object sender, EventArgs e)
    {
        CardCommunity.Visibility = Visibility.Collapsed;
    }
}