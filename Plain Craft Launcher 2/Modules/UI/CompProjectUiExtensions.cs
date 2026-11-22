using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App.Localization;
using PCL.Core.UI.Threading;

namespace PCL;

/// <summary>Comp 工程项的 WPF 控件构造，保留在 UI 程序集。</summary>
public static class CompProjectUiExtensions
{
    private static readonly Brush[] InitialBrushes =
    [
        new SolidColorBrush(Color.FromRgb(0x5B, 0x6E, 0xE1)),
        new SolidColorBrush(Color.FromRgb(0x3D, 0x99, 0x70)),
        new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x3A)),
        new SolidColorBrush(Color.FromRgb(0xC8, 0x50, 0x6B)),
        new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xA4)),
        new SolidColorBrush(Color.FromRgb(0x8E, 0x7C, 0xC3))
    ];

    private static string GetInitial(Comp.CompProject mod)
    {
        var name = string.IsNullOrWhiteSpace(mod.TranslatedName) ? mod.RawName : mod.TranslatedName;
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
            return "?";
        var ch = name[0];
        return char.IsLetter(ch) ? char.ToUpperInvariant(ch).ToString() : ch.ToString();
    }

    private static Brush GetInitialBrush(string key)
    {
        var hash = 0;
        foreach (var c in key ?? string.Empty)
            hash = unchecked(hash * 31 + c);
        return InitialBrushes[Math.Abs(hash) % InitialBrushes.Length];
    }

    extension(Comp.CompProject project)
    {
        public MyVirtualizingElement<MyCompItem> ToCompItem(
            bool showMcVersionDesc,
            bool showLoaderDesc)
        {
            return new MyVirtualizingElement<MyCompItem>(() =>
            {
                var newItem = new MyCompItem
                {
                    Tag = project
                };
                project.ApplyIcon(newItem);

                var title = project.GetControlTitle(true);
                newItem.Title = title.Key;

                if (string.IsNullOrEmpty(title.Value))
                    ((StackPanel)newItem.LabTitleRaw.Parent).Children.Remove(
                        newItem.LabTitleRaw);
                else
                    newItem.SubTitle = title.Value;

                newItem.Tags = project.Tags;
                newItem.Description = project.Description.Replace("\r", "").Replace("\n", "");
                // 构造时同步收藏实心/空心状态，避免已收藏模组在列表里仍显示空心 heart。
                newItem.RefreshFavoriteStatus();

                LocalizedText.TryBind(
                    newItem.LabVersion,
                    "Text",
                    () => project.BuildVersionDescription(showMcVersionDesc, showLoaderDesc));

                if (!showMcVersionDesc && !showLoaderDesc)
                {
                    ((Grid)newItem.SvgIconVersion.Parent).Children.Remove(
                        newItem.SvgIconVersion);
                    ((Grid)newItem.LabVersion.Parent).Children.Remove(newItem.LabVersion);
                    newItem.ColumnVersion1.Width = new GridLength(0);
                    newItem.ColumnVersion2.MaxWidth = 0;
                    newItem.ColumnVersion3.Width = new GridLength(0);
                }

                newItem.LabSource.Text = "CKAN";

                if (project.LastUpdate is not null)
                {
                    var lastUpdate = project.LastUpdate.Value;
                    LocalizedText.TryBind(
                        newItem.LabTime,
                        "Text",
                        () => Lang.TimeSpan(lastUpdate - DateTime.Now, 1));
                }
                else
                {
                    newItem.LabTime.Visibility = Visibility.Collapsed;
                    newItem.ColumnTime1.Width =
                        newItem.ColumnTime2.Width =
                            newItem.ColumnTime3.Width = new GridLength(0);
                }

                LocalizedText.TryBind(
                    newItem.LabDownload,
                    "Text",
                    () => Lang.CompactNumber(project.DownloadCount));

                return newItem;
            })
            {
                Height = 64
            };
        }

        public MyListItem ToListItem()
        {
            // 命中磁盘缓存直接用本地文件，未命中先用 URL 并排进后台缓存队列
            var logo = string.IsNullOrEmpty(project.LogoUrl)
                ? $"{LauncherPaths.ImageRoot}Icons/NoIcon.png"
                : BannerCache.TryGetLocal(project.LogoUrl) ?? project.LogoUrl;
            if (!string.IsNullOrEmpty(project.LogoUrl) && logo == project.LogoUrl)
                _ = BannerCache.EnsureAsync(project.LogoUrl);

            return new MyListItem
            {
                Title = project.TranslatedName,
                Info = project
                    .Description
                    .Replace("\r", "")
                    .Replace("\n", ""),
                Logo = logo,
                Tags = project.Tags,
                Tag = project,
                LogoCornerRadius = new CornerRadius(6)
            };
        }

        /// <summary>有图标则显示横幅图（方形裁剪）；无图标则用名字首字母色块占位。</summary>
        public void ApplyIcon(MyCompItem item)
        {
            var letterBrushKey = string.IsNullOrWhiteSpace(project.TranslatedName) ? project.RawName : project.TranslatedName;
            item.Letter = GetInitial(project);
            item.LetterBrush = GetInitialBrush(letterBrushKey);
            if (string.IsNullOrEmpty(project.LogoUrl))
            {
                item.PathLogo.Source = null;
                item.ShowLetter = true;
                item.Letter = GetInitial(project);
                item.LetterBrush = GetInitialBrush(string.IsNullOrWhiteSpace(project.TranslatedName) ? project.RawName : project.TranslatedName);
            }
            else
            {
                // 先保持首字母色块；横幅命中本地缓存就直接秒出，未命中才排队下载，下载完再替换。
                item.ShowLetter = true;
                var logoUrl = project.LogoUrl;
                var cachedLogo = BannerCache.TryGetLocal(logoUrl);
                if (cachedLogo is not null)
                {
                    item.PathLogo.Source = cachedLogo;
                }
                else
                {
                    item.PathLogo.Source = null;
                    var target = item;
                    _ = BannerCache.EnsureAsync(logoUrl).ContinueWith(t =>
                    {
                        var path = t.Result;
                        if (string.IsNullOrEmpty(path))
                            return;
                        UiDispatcher.Post(() =>
                        {
                            if (target.PathLogo.Source is null)
                                target.PathLogo.Source = path;
                        });
                    }, TaskScheduler.Default);
                }
            }
        }
    }
}
