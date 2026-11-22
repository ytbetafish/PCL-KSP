using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PCL;

/// <summary>
/// 手写 WPF 对话框共用的外壳：与主界面同一套主题配色的无边框窗口（圆角 + 阴影 + 自绘标题栏 + 关闭按钮）。<br />
/// 裸 <see cref="Window" /> 使用系统白底，而界面里的文字/按钮走主题色，深色模式下会变成白底浅字，这里统一收口。
/// </summary>
internal static class AppDialogWindow
{
    /// <summary>阴影留白：窗口四周留出的透明边距，避免阴影被裁掉。</summary>
    private const double ShadowMargin = 12d;

    /// <summary>
    /// 创建一个与主界面同主题的对话框窗口。
    /// </summary>
    /// <param name="title">标题栏文字。</param>
    /// <param name="width">内容区宽度（不含阴影留白）。</param>
    /// <param name="host">内容宿主，调用方把自己的内容面板塞进 <see cref="ContentPresenter.Content" />。</param>
    /// <param name="modal">是否以 <see cref="Window.ShowDialog" /> 方式显示（关闭按钮 / Esc 会以取消结束）。</param>
    /// <param name="owner">指定 Owner；为空时自动使用主窗口。</param>
    /// <param name="height">固定高度（含阴影留白内）；为空表示由内容撑开高度。</param>
    public static Window Create(
        string title,
        double width,
        out ContentPresenter host,
        bool modal = true,
        Window? owner = null,
        double? height = null)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Width = width + ShadowMargin * 2,
            SizeToContent = height is null ? SizeToContent.Height : SizeToContent.Manual,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        if (height is { } fixedHeight)
        {
            window.Height = fixedHeight + ShadowMargin * 2;
        }

        window.SetResourceReference(Control.FontFamilyProperty, "LaunchFontFamily");

        var ownerWindow = owner ?? Application.Current?.MainWindow;
        if (ownerWindow is { IsVisible: true } && !ReferenceEquals(ownerWindow, window))
        {
            window.Owner = ownerWindow;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var border = new Border { CornerRadius = new CornerRadius(7) };
        border.SetResourceReference(Border.BackgroundProperty, "ColorBrushBackground");
        if (Application.Current?.TryFindResource("ColorObjectMsgBoxShadow") is Color shadowColor)
        {
            border.Effect = new DropShadowEffect
            {
                Color = shadowColor,
                BlurRadius = 18d,
                ShadowDepth = 2d,
                Opacity = 0.55d,
                RenderingBias = RenderingBias.Performance
            };
        }

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46d) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1d) });
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = height is null ? GridLength.Auto : new GridLength(1d, GridUnitType.Star)
        });

        host = new ContentPresenter();
        Grid.SetRow(host, 2);
        layout.Children.Add(host);

        var line = new Rectangle { Height = 1d, Margin = new Thickness(22d, 0d, 22d, 0d) };
        line.SetResourceReference(Shape.FillProperty, "ColorBrush2");
        Grid.SetRow(line, 1);
        layout.Children.Add(line);

        var titleBar = new Grid { Background = Brushes.Transparent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 16d,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22d, 0d, 10d, 0d),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        var btnClose = new MyIconButton
        {
            SvgIcon = "lucide/x",
            Width = 22d,
            Height = 22d,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0d, 0d, 14d, 0d),
            ToolTip = "关闭"
        };
        Grid.SetColumn(btnClose, 1);
        titleBar.Children.Add(btnClose);

        Grid.SetRow(titleBar, 0);
        layout.Children.Add(titleBar);

        void CloseAsCancel()
        {
            if (!window.IsVisible) return;
            if (modal)
            {
                try
                {
                    window.DialogResult = false;
                }
                catch (InvalidOperationException)
                {
                    window.Close();
                }
            }
            else
            {
                window.Close();
            }
        }

        btnClose.Click += (_, _) => CloseAsCancel();
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // 窗口尚未完成显示时拖动会抛异常，忽略即可。
            }
        };
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseAsCancel();
        };

        border.Child = layout;
        window.Content = new Grid { Margin = new Thickness(ShadowMargin), Children = { border } };
        return window;
    }
}