using System.Linq;

namespace PCL;

/// <summary>
/// 主页预设清单。Url 为空表示由启动器本地渲染，否则从该地址联网获取 XAML（与原版一致）。
/// 仅收录与 Minecraft 无关的原版预设；MC 相关预设按项目要求不接入。
/// </summary>
public sealed class HomePagePreset
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public string? Url { get; init; }

    public static readonly HomePagePreset[] All =
    [
        new() { Id = 0, Title = "PCL-KSP 内置主页" },
        new() { Id = 1, Title = "你知道吗" },
        new() { Id = 6, Title = "KSP Wiki 随机条目" },
        new() { Id = 2, Title = "Bangumi 番剧主页", Url = "https://bangumi.p.kaphia.qzz.io" },
        new() { Id = 4, Title = "今日新闻热点", Url = "https://pcl.wyc-w.top/index.xaml" }
    ];

    public static HomePagePreset Find(int id)
        => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    public static int Resolve(int id)
        => All.Any(p => p.Id == id) ? id : 0;
}