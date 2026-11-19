using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;
using PCL.Core.UI.Threading;
using PCL.Network;

namespace PCL;

/// <summary>
/// 任务管理页（右下角下载按钮点出来）。原样移植自原版 PCL-CE 的 PageSpeedLeft，
/// 仅把外部依赖替换为 PCL-KSP 现有实现：AniDispose 改为控件上的静态方法，卡片按加载器 Uuid 索引
/// （CKAN 任务的标题会随进度变化），并为非 LoaderCombo 的加载器（CKAN 下载）提供逐行子任务。
/// </summary>
public partial class PageSpeedLeft : IPageView
{
    private const int watcherInterval = 300;

    private readonly PageSpeedLeftViewModel _vm = new();

    private readonly Dictionary<int, MyCard> rightCards = new();

    private DispatcherTimer? _timer;

    public PageSpeedLeft()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    public void OnNavigatedTo(object? parameter)
    {
        UiDispatcher.Post(() =>
        {
            Watcher();
            TryReturnToHome();
        });
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, watcherInterval) };
            _timer.Tick += (_, _) => Watcher();
            if (!Common.modeDebug)
            {
                RowDefinitions[12].Height = new GridLength(0d);
                RowDefinitions[13].Height = new GridLength(0d);
                RowDefinitions[14].Height = new GridLength(0d);
                RowDefinitions[15].Height = new GridLength(0d);
            }
        }

        _timer.Start();
    }

    public void OnNavigatedFrom() => _timer?.Stop();

    private void Watcher()
    {
        if (Launcher.frmMain.pageCurrent != FormMain.PageType.TaskManager)
            return;

        _vm.Refresh();

        if (Launcher.frmSpeedRight is null || Launcher.frmSpeedRight.PanMain is null)
            return;
        try
        {
            foreach (var loader in Loading.loaderTaskbar.ToList())
                TaskRefresh(loader);
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "任务管理右栏监视出错",
                Common.LogLevel.Feedback,
                userSummary: Lang.Text("Speed.Error.OperationFailed"));
        }
    }

    /// <summary>取出该加载器要显示的逐行子任务：LoaderCombo 用其内部列表，CKAN 任务用其模组步骤。</summary>
    private static List<Loading.LoaderBase> GetSubLoaders(Loading.LoaderBase loader)
    {
        switch (loader)
        {
            case Loading.LoaderCombo combo:
                return combo.GetLoaderList();
            case CkanUpdateLoader ckan:
                return ckan.Steps.Cast<Loading.LoaderBase>().ToList();
            default:
                return new List<Loading.LoaderBase> { loader };
        }
    }

    public void TaskRefresh(Loading.LoaderBase loader)
    {
        if (loader is null || !loader.show)
            return;
        try
        {
            // 获取实际加载器列表
            var loaderList = GetSubLoaders(loader);
            if (rightCards.ContainsKey(loader.Uuid))
            {
                // 已有此卡片
                Grid card = rightCards[loader.Uuid];
                var newValue = loader.Progress + (double)loader.State;
                if (Common.Val(card.Tag) == newValue)
                    return;
                card.Tag = newValue;
                if (card.Children.Count <= 3)
                {
                    Common.Log("[Watcher] 元素不足的卡片：" + loader.name, Common.LogLevel.Debug);
                    return;
                }

                card = (Grid)card.Children[3];
                try
                {
                    switch (loader.State)
                    {
                        case LoadState.Failed:
                        {
                            #region 失败，更新卡片

                            card.RowDefinitions.Clear();
                            card.Children.Clear();
                            card.Children.Add((UIElement)Common.GetObjectFromXml(
                                "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Stretch=\"Uniform\" Tag=\"Failed\" Data=\"F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z\" Height=\"15\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"0\" Fill=\"{DynamicResource ColorBrush3}\" Margin=\"0,1,0,0\" VerticalAlignment=\"Top\"/>"));
                            var tb = (TextBlock)Common.GetObjectFromXml(
                                "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextWrapping=\"Wrap\" HorizontalAlignment=\"Left\" Grid.Column=\"1\" Grid.Row=\"0\" Margin=\"0,0,0,5\" />");
                            tb.ToolTip = Lang.Text("Speed.Error.ClickToCopy");
                            tb.Text = loader.Error.ToString();
                            tb.MouseLeftButtonDown += (sender, _) =>
                            {
                                Common.ClipboardSet(((TextBlock)sender).Text, false);
                                HintService.Show(Hints.Success(Lang.Text("Speed.Error.Copied")));
                            };
                            card.Children.Add(tb);
                            break;
                        }

                        #endregion

                        case LoadState.Finished:
                        {
                            #region 完成，销毁卡片并返回

                            rightCards.Remove(loader.Uuid);
                            Animations.AniDispose((MyCard)card.Parent, true, _ => TryReturnToHome());
                            break;
                        }

                        #endregion

                        case LoadState.Loading:
                        case LoadState.Waiting:
                        {
                            #region 进度不同，更新卡片

                            do
                            {
                                try
                                {
                                    if (card.Children.Count < loaderList.Count * 2)
                                    {
                                        Common.Log(
                                            $"[Watcher] 刷新任务管理卡片 {loader.name} 失败：卡片中仅有 {card.Children.Count} 个子项，要求至少有 {loaderList.Count * 2} 个子项",
                                            Common.LogLevel.Debug);
                                        break;
                                    }

                                    var row = 0;
                                    foreach (var SubTask in loaderList)
                                    {
                                        switch (SubTask.State)
                                        {
                                            case LoadState.Waiting:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Waiting")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)Common.GetObjectFromXml(
                                                            "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=PCL-KSP\" Stretch=\"Uniform\" Tag=\"Waiting\" Data=\"F1 M5,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 Z\" Width=\"18\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"" +
                                                            row +
                                                            "\" Fill=\"{DynamicResource ColorBrush3}\" Margin=\"0,7,0,0\" VerticalAlignment=\"Top\" Height=\"6\"/>"));
                                                }

                                                break;
                                            }
                                            case LoadState.Loading:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Loading")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)Common.GetObjectFromXml(
                                                            $"<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Text=\"{Lang.Number(SubTask.Progress, "P0")}\" Tag=\"Loading\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Foreground=\"{{DynamicResource ColorBrush3}}\"/>"));
                                                }
                                                else
                                                {
                                                    ((TextBlock)card.Children[row * 2]).Text =
                                                        $"{Lang.Number(SubTask.Progress, "P0")}";
                                                }

                                                break;
                                            }
                                            case LoadState.Finished:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Finished")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)Common.GetObjectFromXml(
                                                            $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Stretch=\"Uniform\" Tag=\"Finished\" Data=\"F1 M 23.7501,33.25L 34.8334,44.3333L 52.2499,22.1668L 56.9999,26.9168L 34.8334,53.8333L 19.0001,38L 23.7501,33.25 Z\" Height=\"16\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,3,0,0\" VerticalAlignment=\"Top\"/>"));
                                                }

                                                break;
                                            }
                                            default:
                                            {
                                                if ((string)((FrameworkElement)card.Children[row * 2]).Tag != "Failed")
                                                {
                                                    card.Children.RemoveAt(row * 2);
                                                    card.Children.Insert(row * 2,
                                                        (UIElement)Common.GetObjectFromXml(
                                                            "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Stretch=\"Uniform\" Tag=\"Failed\" Data=\"F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z\" Height=\"15\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"" +
                                                            row +
                                                            "\" Fill=\"{DynamicResource ColorBrush3}\" Margin=\"0,1,0,0\" VerticalAlignment=\"Top\"/>"));
                                                }

                                                break;
                                            }
                                        }

                                        row += 1;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Common.Log(
                                        ex,
                                        $"刷新任务管理卡片 {loader.name} 失败",
                                        Common.LogLevel.Feedback,
                                        userSummary: Lang.Text("Speed.Error.OperationFailed"));
                                }
                            } while (false);

                            break;
                        }

                        #endregion
                    }
                }
                catch (Exception ex)
                {
                    Common.Log(
                        ex,
                        $"更新任务管理显示失败（{loader.State}）",
                        Common.LogLevel.Feedback,
                        userSummary: Lang.Text("Speed.Error.OperationFailed"));
                }
            }
            else if (!(loader.State == LoadState.Aborted || loader.State == LoadState.Finished))
            {
                try
                {
                    #region 没有卡片且未中断或完成，添加新的卡片

                    var cardXAML = $@"
                        <local:MyCard xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=PCL-KSP""
                            Tag=""{loader.Progress + (double)loader.State}"" Title=""{Common.EscapeXml(loader.name)}"" Margin=""0,0,0,15"">
                            <Grid Margin=""14,40,15,10"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""50""/>
                                    <ColumnDefinition/>
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>";
                    foreach (var SubTask in loaderList)
                        cardXAML += "<RowDefinition Height=\"26\"/>";
                    cardXAML += "</Grid.RowDefinitions>";
                    var row = 0;
                    foreach (var SubTask in loaderList)
                    {
                        switch (SubTask.State)
                        {
                            case LoadState.Waiting:
                            {
                                cardXAML +=
                                    $"<Path Stretch=\"Uniform\" Tag=\"Waiting\" Data=\"F1 M5,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 Z\" Width=\"18\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,7,0,0\" VerticalAlignment=\"Top\" Height=\"6\"/>";
                                break;
                            }
                            case LoadState.Loading:
                            {
                                cardXAML += $"<TextBlock Text=\"{Lang.Number(SubTask.Progress, "P0")}\" Tag=\"Loading\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Foreground=\"{{DynamicResource ColorBrush3}}\" />";
                                break;
                            }
                            case LoadState.Finished:
                            {
                                cardXAML +=
                                    $"<Path Stretch=\"Uniform\" Tag=\"Finished\" Data=\"F1 M 23.7501,33.25L 34.8334,44.3333L 52.2499,22.1668L 56.9999,26.9168L 34.8334,53.8333L 19.0001,38L 23.7501,33.25 Z\" Height=\"16\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,3,0,0\" VerticalAlignment=\"Top\"/>";
                                break;
                            }

                            default:
                            {
                                cardXAML +=
                                    $"<Path Stretch=\"Uniform\" Tag=\"Failed\" Data=\"F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z\" Height=\"15\" Width=\"15\" HorizontalAlignment=\"Center\" Grid.Column=\"0\" Grid.Row=\"{row}\" Fill=\"{{DynamicResource ColorBrush3}}\" Margin=\"0,1,0,0\" VerticalAlignment=\"Top\"/>";
                                break;
                            }
                        }

                        cardXAML += $"<TextBlock Text=\"{Common.EscapeXml(SubTask.name)}\" HorizontalAlignment=\"Left\" Grid.Column=\"1\" Grid.Row=\"{row}\"/>";
                        row += 1;
                    }

                    cardXAML += "</Grid></local:MyCard>";
                    // 实例化控件
                    MyCard card;
                    try
                    {
                        card = (MyCard)Common.GetObjectFromXml(cardXAML);
                    }
                    catch (Exception ex)
                    {
                        Common.Log(ex, "新建任务管理卡片失败");
                        Common.Log($"出错的卡片内容：\r\n{cardXAML}");
                        throw;
                    }

                    Launcher.frmSpeedRight.PanMain.Children.Insert(0, card);
                    rightCards.Add(loader.Uuid, card);
                    Common.Log($"[Watcher] 新建任务管理卡片：{loader.name}");
                    // 添加取消按钮
                    var cancel = new MyIconButton
                    {
                        Name = "BtnCancel",
                        SvgIcon = "lucide/x", Height = 25d, Width = 25d,
                        Margin = new Thickness(0d, 10d, 10d, 0d), LogoScale = 1.1d,
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
                    };
                    card.Children.Add(cancel);
                    cancel.Click += (sender, e) =>
                    {
                        Animations.AniDispose((MyIconButton)sender, false);
                        var loaderUuid = loader.Uuid;
                        Animations.AniDispose(card, true, _ =>
                        {
                            if (Launcher.frmSpeedRight.PanMain.Children.Count == 0 &&
                                Launcher.frmMain.pageCurrent == FormMain.PageType.TaskManager)
                                Launcher.frmMain.PageBack();
                        });
                        rightCards.Remove(loaderUuid);
                        Loading.loaderTaskbar.Remove(loader);
                        Common.Log($"[Taskbar] 关闭任务管理卡片：{loader.name}，且移出任务列表");
                        Common.RunInThread(() => loader.Abort());
                    };
                    // 如果已经失败，再刷新一次，修改成失败的控件
                    if (loader.State == LoadState.Failed)
                    {
                        card.Tag = null; // 避免重复导致刷新无效
                        TaskRefresh(loader);
                    }
                }

                #endregion

                catch (Exception ex)
                {
                    Common.Log(
                        ex,
                        "添加任务管理卡片失败",
                        Common.LogLevel.Feedback,
                        userSummary: Lang.Text("Speed.Error.OperationFailed"));
                }
            }
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "刷新任务管理显示失败",
                Common.LogLevel.Feedback,
                userSummary: Lang.Text("Speed.Error.OperationFailed"));
        }
    }

    public void TaskRemove(Loading.LoaderBase loader)
    {
        if (rightCards.ContainsKey(loader.Uuid))
            Common.RunInUiWait(() =>
            {
                // 移除已有的卡片
                MyCard card = rightCards[loader.Uuid];
                Launcher.frmSpeedRight.PanMain.Children.Remove(card);
                rightCards.Remove(loader.Uuid);
                Common.Log($"[Watcher] 移除任务管理卡片：{loader.name}");
            });
    }

    /// <summary>
    ///     若没有任务，尝试返回主页。
    /// </summary>
    private void TryReturnToHome()
    {
        if (Launcher.frmSpeedRight.PanMain.Children.Count == 0 &&
            Launcher.frmMain.pageCurrent == FormMain.PageType.TaskManager) Launcher.frmMain.PageBack();
    }
}
