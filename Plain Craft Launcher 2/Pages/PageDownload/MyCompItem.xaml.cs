using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App.Localization;

namespace PCL;

public partial class MyCompItem
{
    private readonly MyCompItemViewModel _vm = new();

    public MyCompItemViewModel ViewModel => _vm;

    private string _stateLast;

    public bool CanInteraction { get; set; } = true;

    public void RefreshColor(object sender, EventArgs e)
    {
        if (!CanInteraction)
            return;
        string stateNew;
        int time;
        if (IsMouseOver)
        {
            if (_isMouseDown)
            {
                stateNew = "MouseDown";
                time = 120;
            }
            else
            {
                stateNew = "MouseOver";
                time = 120;
            }
        }
        else
        {
            stateNew = "Idle";
            time = 180;
        }

        if ((_stateLast ?? "") == (stateNew ?? ""))
            return;
        _stateLast = stateNew;

        if (IsLoaded && !Animations.AniExitsAnyControl())
        {
            var ani = new List<Animations.AniData>();
            if (IsMouseOver)
            {
                if (PanButtons is not null && _HasActionButtons)
                    ani.Add(Animations.AaOpacity(PanButtons, 1d - PanButtons.Opacity,
                        (int)Math.Round(time * 0.35d),
                        (int)Math.Round(time * 0.15d)));
                ani.AddRange(new[]
                {
                    Animations.AaColor(RectBack, Border.BackgroundProperty,
                        _isMouseDown ? "ColorBrush6" : "ColorBrushBg1", time),
                    Animations.AaOpacity(RectBack, 1d - RectBack.Opacity, time,
                        ease: new Animations.AniEaseOutFluent())
                });
                if (_isMouseDown)
                    ani.Add(Animations.AaScaleTransform(RectBack,
                        0.996d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, (int)Math.Round(time * 1.2d),
                        ease: new Animations.AniEaseOutFluent()));
                else
                    ani.Add(Animations.AaScaleTransform(RectBack,
                        1d - ((ScaleTransform)RectBack.RenderTransform).ScaleX, (int)Math.Round(time * 1.2d),
                        ease: new Animations.AniEaseOutFluent()));
            }
            else
            {
                if (PanButtons is not null && _HasActionButtons)
                    ani.Add(Animations.AaOpacity(PanButtons, -PanButtons.Opacity,
                        (int)Math.Round(time * 0.4d)));
                ani.AddRange(new[]
                {
                    Animations.AaOpacity(RectBack, -RectBack.Opacity, time),
                    Animations.AaColor(RectBack, Border.BackgroundProperty,
                        _isMouseDown ? "ColorBrush6" : "ColorBrush7", time),
                    Animations.AaScaleTransform(RectBack,
                        0.996d - ((ScaleTransform)RectBack.RenderTransform).ScaleX,
                        time, ease: new Animations.AniEaseOutFluent()),
                    Animations.AaScaleTransform(RectBack, -0.196d, 1, after: true)
                });
            }

            Animations.AniStart(ani, "CompItem Color " + Uuid);
        }
        else
        {
            Animations.AniStop("CompItem Color " + Uuid);
            if (_rectBack is not null)
                RectBack.Opacity = 0d;
            if (PanButtons is not null)
                PanButtons.Opacity = 0d;
        }
    }

    #region 基础属性

    public int Uuid = Common.GetUuid();

    #region 勾选状态（对齐原版 MyListItem 的 Checked / Changed / rectCheck 竖条）

    public delegate void ChangedEventHandler(MyCompItem sender, Common.RouteEventArgs e);

    /// <summary>勾选状态发生变化时触发（含程序化与用户操作，由参数区分）。</summary>
    public event ChangedEventHandler? Changed;

    /// <summary>是否处于可勾选模式。关闭时保持原有“单击进详情”行为不变。</summary>
    public bool IsCheckable { get; set; }

    /// <summary>供页面记录选中项所用的唯一键（实例页为模组 Identifier）。</summary>
    public string? SelectKey { get; set; }

    private bool _checked;

    public bool Checked => _checked;

    /// <summary>
    /// 设置勾选状态。
    /// </summary>
    /// <param name="value">是否勾选。</param>
    /// <param name="user">是否由用户操作引发。</param>
    /// <param name="anime">是否播放竖条动画。</param>
    public void SetChecked(bool value, bool user, bool anime)
    {
        try
        {
            var rawValue = _checked;
            if (value == _checked)
                return;
            _checked = value;

            var changedEventArgs = new Common.RouteEventArgs(user);
            if (IsInitialized)
            {
                Changed?.Invoke(this, changedEventArgs);
                if (changedEventArgs.handled)
                {
                    _checked = rawValue;
                    return;
                }
            }

            RefreshCheckedVisual(value, anime);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "设置 Checked 失败");
        }
    }

    /// <summary>复刻原版 MyListItem.RefreshCheckedVisual：左侧竖条 ScaleY 回弹 / 收缩淡出。</summary>
    private void RefreshCheckedVisual(bool value, bool anime)
    {
        if (RectCheck is null)
            return;

        if (IsLoaded && !Animations.AniExitsAnyControl() && anime)
        {
            var anim = new List<Animations.AniData>();
            if (value)
            {
                // 由无变有：竖条 ScaleY 从 0 弹性放大到 1
                RectCheck.Height = 20d;
                RectCheck.VerticalAlignment = VerticalAlignment.Center;
                RectCheck.Margin = new Thickness(-1, 0d, 0d, 0d);
                RectCheck.Opacity = 1d;

                var scale = new ScaleTransform(1d, 0d);
                RectCheck.RenderTransformOrigin = new Point(0.5d, 0.5d);
                RectCheck.RenderTransform = scale;

                anim.Add(Animations.AaDouble(
                    i => scale.ScaleY = Math.Max(0d, scale.ScaleY + (double)i),
                    1d - scale.ScaleY,
                    300,
                    ease: new Animations.AniEaseOutBack(Animations.AniEasePower.Weak)));
            }
            else
            {
                // 由有变无：竖条 ScaleY 收缩到 0 并淡出
                if (RectCheck.RenderTransform is not ScaleTransform)
                {
                    RectCheck.RenderTransformOrigin = new Point(0.5d, 0.5d);
                    RectCheck.RenderTransform = new ScaleTransform(1d, 1d);
                }

                var scale = (ScaleTransform)RectCheck.RenderTransform;
                anim.Add(Animations.AaDouble(
                    i => scale.ScaleY = Math.Max(0d, scale.ScaleY + (double)i),
                    -scale.ScaleY,
                    120,
                    ease: new Animations.AniEaseInFluent(Animations.AniEasePower.Weak)));
                anim.Add(Animations.AaOpacity(RectCheck, -RectCheck.Opacity, 70, 40));
            }

            Animations.AniStart(anim, "MyCompItem Checked " + Uuid);
        }
        else
        {
            Animations.AniStop("MyCompItem Checked " + Uuid);
            if (value)
            {
                RectCheck.Height = 20d;
                RectCheck.Margin = new Thickness(-1, 0d, 0d, 0d);
                RectCheck.Opacity = 1d;
                RectCheck.VerticalAlignment = VerticalAlignment.Center;
                RectCheck.RenderTransform = null;
            }
            else
            {
                RectCheck.Height = 0d;
                RectCheck.Margin = new Thickness(-1, 0d, 0d, 0d);
                RectCheck.Opacity = 0d;
                RectCheck.VerticalAlignment = VerticalAlignment.Center;
                RectCheck.RenderTransform = null;
            }
        }
    }

    #endregion

    public string Logo
    {
        get => PathLogo.Source;
        set => PathLogo.Source = value;
    }

    /// <summary>首字母占位文本。</summary>
    public string Letter
    {
        get => LabLetter.Text;
        set => LabLetter.Text = value;
    }

    /// <summary>首字母占位块的背景色。</summary>
    public Brush LetterBrush
    {
        set => PanLetter.Background = value;
    }

    /// <summary>切换图标层与首字母占位层的可见性。</summary>
    public bool ShowLetter
    {
        set
        {
            PanLetter.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            PathLogo.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string Title
    {
        get => _vm.TitleText;
        set
        {
            if ((_vm.TitleText ?? "") == (value ?? ""))
                return;
            _vm.TitleText = value;
        }
    }

    public string SubTitle
    {
        get => _vm.SubTitleText;
        set
        {
            if ((_vm.SubTitleText ?? "") == (value ?? ""))
                return;
            _vm.SubTitleText = value;
            _vm.SubTitleVisibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string Description
    {
        get => _vm.DescriptionText;
        set
        {
            if ((_vm.DescriptionText ?? "") == (value ?? ""))
                return;
            _vm.DescriptionText = value;
        }
    }

    public string Version
    {
        get => LabVersion.Text;
        set => LabVersion.Text = value;
    }

    public string Downloads
    {
        get => LabDownload.Text;
        set => LabDownload.Text = value;
    }

    public string UpdateTime
    {
        get => LabTime.Text;
        set => LabTime.Text = value;
    }

    public string Source
    {
        get => LabSource.Text;
        set => LabSource.Text = value;
    }

    /// <summary>是否显示版本/下载/时间/来源元数据行（下载页保留，实例页可隐藏）。</summary>
    public bool ShowMetaRow
    {
        set => PanMetaRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public MyCompItem()
    {
        InitializeComponent();
        Click += (sender, e) => MyCompItem_Click((MyCompItem)sender, e);
        PreviewMouseLeftButtonUp += Button_MouseUp;
        PreviewMouseLeftButtonDown += Button_MouseDown;
        MouseLeave += Button_MouseLeave;
        PreviewMouseLeftButtonUp += Button_MouseLeave;
        MouseEnter += RefreshColor;
        MouseLeave += RefreshColor;
        MouseLeftButtonDown += RefreshColor;
        MouseLeftButtonUp += RefreshColor;
        PathLogo.ImageLoadFailed += OnLogoLoadFailed;
        PathLogo.ImageLoaded += OnLogoLoaded;
        // 右键任意位置进入详情（对齐原版：sender.MouseRightButtonUp += Info_Click）
        MouseRightButtonUp += MyCompItem_RightClick;
    }

    /// <summary>右键进入模组详情页（与详情按钮、非勾选模式单击同一入口）。</summary>
    private void MyCompItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenDetail();
    }


    /// <summary>图片下载失败时，若已配置首字母占位则切回首字母色块。</summary>
    private void OnLogoLoadFailed()
    {
        if (!string.IsNullOrEmpty(LabLetter.Text))
            ShowLetter = true;
    }

    /// <summary>图片成功加载后切换到图标层；未配置首字母时不处理。</summary>
    private void OnLogoLoaded()
    {
        if (!string.IsNullOrEmpty(LabLetter.Text))
            ShowLetter = false;
    }

    private void LabInfo_MouseEnter(object sender, MouseEventArgs e)

    {
        if (IsTextTrimmed(LabInfo))
        {
            ToolTipInfo.Content = LabInfo.Text;
            ToolTipInfo.Width = LabInfo.ActualWidth + 25d;
            LabInfo.ToolTip = ToolTipInfo;
        }
        else
        {
            LabInfo.ToolTip = null;
        }
    }

    private bool IsTextTrimmed(TextBlock textBlock)
    {
        var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight,
            textBlock.FontStretch);
        var formattedText = new FormattedText(textBlock.Text, Thread.CurrentThread.CurrentCulture,
            textBlock.FlowDirection, typeface, textBlock.FontSize, textBlock.Foreground,
            Common.Dpi);
        return formattedText.Width > textBlock.ActualWidth;
    }

    public List<string> Tags
    {
        set => TagPanelHelper.FillLocalized(
            PanTags,
            value,
            new SolidColorBrush(Color.FromRgb(134, 134, 134)));
    }

    public bool ShowFavoriteBtn
    {
        get => BtnDelete.Visibility == Visibility.Visible;
        set
        {
            BtnDelete.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _UpdatePanButtons();
        }
    }

    public bool ShowDownloadBtn
    {
        get => BtnDownload.Visibility == Visibility.Visible;
        set
        {
            BtnDownload.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _UpdatePanButtons();
        }
    }

    /// <summary>是否显示详情（info）按钮。可勾选模式下用它作为进模组详情的明确入口。</summary>
    public bool ShowDetailBtn
    {
        get => BtnDetail.Visibility == Visibility.Visible;
        set
        {
            BtnDetail.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _UpdatePanButtons();
        }
    }

    private bool _HasActionButtons => ShowFavoriteBtn || ShowDownloadBtn || ShowDetailBtn;

    private void _UpdatePanButtons()
    {
        if (PanButtons is null) return;
        PanButtons.Visibility = _HasActionButtons ? Visibility.Visible : Visibility.Collapsed;
    }

    public void RefreshFavoriteStatus()
    {
        if (Tag is Comp.CompProject project)
        {
            var isFavourite = Comp.CompFavorites.IsFavourite(project.Id);
            BtnDelete.SvgIcon = isFavourite ? "lucide/heart-filled" : "lucide/heart";
        }
    }

    /// <summary>
    /// 回填完成后原地刷新卡片显示，不重建可视化树，避免整表 Clear+Add 造成的闪烁。
    /// </summary>
    public void RefreshFrom(Comp.CompProject project, bool showMcVersionDesc, bool showLoaderDesc)
    {
        Tag = project;
        project.ApplyIcon(this);

        var title = project.GetControlTitle(true);
        Title = title.Key;
        if (string.IsNullOrEmpty(title.Value))
        {
            if (LabTitleRaw.Parent is StackPanel rawPanel && rawPanel.Children.Contains(LabTitleRaw))
                rawPanel.Children.Remove(LabTitleRaw);
        }
        else
        {
            SubTitle = title.Value;
        }

        Tags = project.Tags;
        Description = project.Description.Replace("\r", "").Replace("\n", "");

        Version = project.BuildVersionDescription(showMcVersionDesc, showLoaderDesc);
        if (!showMcVersionDesc && !showLoaderDesc)
        {
            if (SvgIconVersion.Parent is Grid iconGrid && iconGrid.Children.Contains(SvgIconVersion))
                iconGrid.Children.Remove(SvgIconVersion);
            if (LabVersion.Parent is Grid versionGrid && versionGrid.Children.Contains(LabVersion))
                versionGrid.Children.Remove(LabVersion);
            ColumnVersion1.Width = new GridLength(0);
            ColumnVersion2.MaxWidth = 0;
            ColumnVersion3.Width = new GridLength(0);
        }

        Source = project.IsCompatible ? "CKAN" : "CKAN（不兼容）";
        if (!project.IsCompatible)
        {
            ToolTip = $"该模组声明最高兼容 KSP {project.MaxKsp ?? "未知"}，与当前实例不兼容；安装时会再次确认。";
        }
        if (!string.IsNullOrEmpty(project.ReplacedById))
        {
            Source = "CKAN（可替换）";
            ToolTip = $"已装版本已被「{project.ReplacedById}」替换；安装后将自动卸载旧模组。";
        }

        if (project.LastUpdate is not null)
            UpdateTime = Lang.TimeSpan(project.LastUpdate.Value - DateTime.Now, 1);
        else
        {
            LabTime.Visibility = Visibility.Collapsed;
            ColumnTime1.Width = ColumnTime2.Width = ColumnTime3.Width = new GridLength(0);
        }

        Downloads = Lang.CompactNumber(project.DownloadCount);
    }
    #endregion

    #region 点击

    public event ClickEventHandler? Click;

    public delegate void ClickEventHandler(object sender, MouseButtonEventArgs e);

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (Tag is Comp.CompProject project)
            CompFavoritesUi.ShowMenu(project, (UIElement)sender, RefreshFavoriteStatus);
    }

    private void _BtnDownload_Click(object sender, EventArgs e)
    {
    }

    private void MyCompItem_Click(MyCompItem sender, EventArgs e)
    {
        if (IsCheckable)
        {
            // 可勾选模式：单击切换选中（对齐原版本地模组“单击勾选”），详情由 info 按钮进入。
            SetChecked(!Checked, true, true);
            return;
        }
        OpenDetail();
    }

    /// <summary>进入模组详情页（保留原有导航，供详情按钮与非勾选模式单击调用）。</summary>
    private void OpenDetail()
    {
        if (Tag is Comp.CompProject project)
        {
            Launcher.frmMain?.PageChange(new FormMain.PageStackData
            {
                page = FormMain.PageType.CompDetail,
                additional = (
                    project,
                    new List<string>(),
                    string.Empty,
                    Comp.CompLoaderType.Any,
                    Comp.CompType.Mod,
                    string.Empty)
            });
        }
    }

    private void BtnDetail_Click(object sender, EventArgs e) => OpenDetail();

    private bool _isMouseDown;

    private void Button_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseDown)
            return;
        Click?.Invoke(sender, e);
    }

    private void Button_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanInteraction)
            return;
        var clickPosition = e.GetPosition(this);
        var isClickOnButton = false;

        if (PanButtons.Visibility == Visibility.Visible)
            isClickOnButton = _IsClickOnActionButton(BtnDetail, clickPosition) ||
                              _IsClickOnActionButton(BtnDelete, clickPosition) ||
                              _IsClickOnActionButton(BtnDownload, clickPosition);

        if (isClickOnButton) return;

        // 整张卡片均可点击：只排除右侧操作按钮。
        // （原版用 IsMouseDirectlyOver 判定，命中标题/图标等不同部位时结果不一致，会出现“点文字没反应、点图标有反应”。）
        _isMouseDown = true;
    }

    private void Button_MouseLeave(object sender, object e)
    {
        _isMouseDown = false;
    }

    private bool _IsClickOnActionButton(FrameworkElement button, Point clickPosition)
    {
        if (button is null || button.Visibility != Visibility.Visible) return false;
        var bounds = new Rect(button.TranslatePoint(new Point(0d, 0d), this), button.RenderSize);
        return bounds.Contains(clickPosition);
    }

    #endregion

    #region 后加载指向背景

    private Border _rectBack;

    public Border RectBack
    {
        get
        {
            if (_rectBack is null)
            {
                var rect = new Border
                {
                    Name = "RectBack",
                    CornerRadius = new CornerRadius(3d),
                    RenderTransform = new ScaleTransform(0.8d, 0.8d),
                    RenderTransformOrigin = new Point(0.5d, 0.5d),
                    BorderThickness = new Thickness(Common.GetWpfSize(1d)),
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false,
                    Opacity = 0d
                };
                rect.SetResourceReference(Border.BackgroundProperty, "ColorBrush7");
                rect.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6");
                SetColumnSpan(rect, 999);
                SetRowSpan(rect, 999);
                Children.Insert(0, rect);
                _rectBack = rect;
            }

            return _rectBack;
        }
    }

    #endregion
}
