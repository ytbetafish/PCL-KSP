using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI.Theme;

namespace PCL;

public partial class Setup
{
    /// <summary>
    ///     将布尔值转换为控件可见性。
    /// </summary>
    private static Visibility ToVisibility(bool visible)
    {
        return visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public static void UiLauncherTransparent(int value)
    {
        Launcher.frmMain?.Opacity = value / 1000d + 0.4d;
    }

    public static void UiLauncherTheme(int value)
    {
        ThemeManager.ThemeRefresh(value);
    }

    public static void UiBackgroundColorful(bool value)
    {
        ThemeManager.ThemeRefresh();
    }

    public static void UiLockWindowSize(bool value)
    {
        if (value)
            Launcher.frmMain?.RemoveResizer();
        else
            Launcher.frmMain?.AddResizer();
    }

    public static void UiAutoPauseVideo(bool value)
    {
        // KSP 改造后：原 MC 主页视频背景（VideoBackground）已剥离，无后台视频需暂停。
    }

    public static void UiBackgroundOpacity(int value)
    {
        Launcher.frmMain?.ImgBack.Opacity = value / 1000d;
    }

    public static void UiBackgroundBlur(int value)
    {
        var background = Launcher.frmMain?.ImgBack;
        if (background is null)
            return;

        background.Effect = value == 0
            ? null
            : new BlurEffect
            {
                Radius = value + 1
            };

        background.Margin = new Thickness(-(value + 1) / 1.8d);
    }

    /// <summary>
    ///     设置背景图片的布局方式。
    /// </summary>
    public static void UiBackgroundSuit(int value)
    {
        var background = Launcher.frmMain?.ImgBack;
        if (background?.Background is null)
            return;

        var brush = (ImageBrush)background.Background;
        var width = brush.ImageSource.Width;
        var height = brush.ImageSource.Height;

        if (value == 0)
        {
            var main = Launcher.frmMain?.PanMain;
            if (main is null)
                return;
            value = width < main.ActualWidth / 2d && height < main.ActualHeight / 2d ? 4 : 2;
        }

        brush.TileMode = TileMode.None;
        brush.Viewport = new Rect(0d, 0d, 1d, 1d);
        brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;

        switch (value)
        {
            case 1 or 5 or 6 or 7 or 8:
                background.HorizontalAlignment = value switch
                {
                    1 => HorizontalAlignment.Center,
                    5 or 7 => HorizontalAlignment.Left,
                    _ => HorizontalAlignment.Right
                };

                background.VerticalAlignment = value switch
                {
                    1 => VerticalAlignment.Center,
                    5 or 6 => VerticalAlignment.Top,
                    _ => VerticalAlignment.Bottom
                };

                brush.Stretch = Stretch.None;
                background.Width = width;
                background.Height = height;
                break;

            case 2 or 3 or 4:
                background.HorizontalAlignment = HorizontalAlignment.Stretch;
                background.VerticalAlignment = VerticalAlignment.Stretch;
                background.Width = double.NaN;
                background.Height = double.NaN;

                brush.Stretch = value switch
                {
                    2 => Stretch.UniformToFill,
                    3 => Stretch.Fill,
                    _ => Stretch.None
                };

                if (value == 4)
                {
                    brush.TileMode = TileMode.Tile;
                    brush.Viewport = new Rect(0d, 0d, width, height);
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                }

                break;
        }
    }

    public static void UiFont(string value)
    {
        try
        {
            Common.SetLaunchFont(value);
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "字体加载失败",
                Common.LogLevel.Hint,
                userSummary: Lang.Text("Setup.Error.OperationFailed"));
        }
    }

    /// <summary>
    ///     设置自定义主页类型。
    ///     KSP 改造后：原 MC 自定义主页设置页（PageSetupUI）已剥离，此设置不再作用于任何 UI。
    /// </summary>
    public static void UiCustomType(int value)
    {
        // KSP 改造后：MC 自定义主页设置界面已剥离。
    }

    public static void UiBlur(bool value)
    {
        UiBlurValue(value ? Config.Preference.Blur.Radius : 0);
    }

    public static void UiBlurValue(int value)
    {
        System.Windows.Application.Current.Resources["BlurRadius"] = (double)value;
    }

    public static void UiBlurSamplingRate(int value)
    {
        System.Windows.Application.Current.Resources["BlurSamplingRate"] = value * 0.01d;
    }

    public static void UiBlurType(int value)
    {
        System.Windows.Application.Current.Resources["BlurType"] = (KernelType)value;
    }

    /// <summary>
    ///     设置标题栏 Logo 类型。
    ///     0：无，1：默认，2：文本，3：图片，4：HMCL。
    /// </summary>
    public static void UiLogoType(int value)
    {
        if (ThemeService.CurrentTheme == ColorTheme.HmclBlue)
            value = 4;

        var main = Launcher.frmMain;
        if (main is null)
            return;

        if (value is >= 0 and <= 4)
        {
            // PCL-KSP 品牌：固定显示文本 logo，隐藏原版矢量/图片/HMCL logo 与 CE 角标。
            main.ShapeTitleLogo.Visibility = Visibility.Collapsed;
            main.ShapeHMCLTitleLogo.Visibility = Visibility.Collapsed;
            main.BtnTitleHelp.Visibility = Visibility.Collapsed;
            main.ImageTitleLogo.Visibility = Visibility.Collapsed;
            main.CELogo.Visibility = Visibility.Collapsed;
            main.LabTitleLogo.Visibility = Visibility.Visible;
        }

        UiLogoLeft(Config.Preference.TopBarLeftAlign);
    }

    /// <summary>
    ///     标题栏默认品牌标题：PCL-KSP 品牌名 + 自有版本号（不含上游版本）。
    /// </summary>
    public static string BrandTitle => "PCL KSP 修改版 " + Basics.VersionName;

    public static void UiLogoText(string value)
    {
        // PCL-KSP 品牌：空文本或残留的原版品牌名回退为默认品牌标题（含自有版本号）。
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 ||
            text.Equals("PCL", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("PCL CE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("PCL-CE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Plain Craft Launcher 2", StringComparison.OrdinalIgnoreCase))
        {
            text = "PCL KSP 修改版";
        }
        Launcher.frmMain?.LabTitleLogo.Text = text;
    }

    public static void UiLogoLeft(bool value)
    {
        var width = value && Config.Preference.WindowTitleType == LauncherTitleType.None
            ? 0d
            : 1d;

        Launcher.frmMain?.PanTitleMain.ColumnDefinitions[0].Width =
            new GridLength(width, GridUnitType.Star);
    }
}
