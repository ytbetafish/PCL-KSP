using PCL.Core.App.Localization;

namespace PCL;

public static partial class Comp
{
    public partial class CompProject
    {
        /// <summary>
        ///     PCL-KSP: 已移除 MC 百科数据库关联，KSP 工程没有百科页面 ID。
        /// </summary>
        public int WikiId => 0;

        /// <summary>
        ///     PCL-KSP: 已移除 MC 百科中文名映射，直接使用原始名称。
        /// </summary>
        public string TranslatedName => RawName;

        /// <summary>
        ///     按当前界面语言生成卡片下边栏的版本描述文本。
        ///     PCL-KSP: 版本描述来自 CKAN 元数据声明的最高兼容 KSP 版本。
        /// </summary>
        /// <param name="showMcVersionDesc">是否显示游戏版本描述。</param>
        /// <param name="showLoaderDesc">是否显示加载器描述。</param>
        /// <returns>应当显示的版本描述；两者都为 <see langword="false" /> 时返回空字符串。</returns>
        public string BuildVersionDescription(bool showMcVersionDesc, bool showLoaderDesc)
        {
            var loaderDesc = showLoaderDesc && ModLoaders.Count != 0
                ? string.Join(" / ", ModLoaders)
                : "";
            if (!showMcVersionDesc)
                return loaderDesc;

            var gameDesc = string.IsNullOrEmpty(MaxKsp) ? "KSP" : "KSP " + MaxKsp;
            return string.IsNullOrEmpty(loaderDesc) ? gameDesc : $"{loaderDesc} {gameDesc}";
        }

        /// <summary>
        ///     生成卡片主标题与副标题。
        /// </summary>
        public KeyValuePair<string, string> GetControlTitle(bool hasModLoaderDescription)
        {
            if (!_TryGetControlTitleParts(out var title, out var subtitleList))
                return new KeyValuePair<string, string>(title, "");

            var subtitleResult = _BuildControlSubtitle(subtitleList, hasModLoaderDescription);
            return new KeyValuePair<string, string>(title, subtitleResult);
        }

        private bool _TryGetControlTitleParts(out string title, out List<string> subtitleList)
        {
            subtitleList = [];

            var nameLists = TranslatedName
                .Split(
                    [" | ", " - ", "(", ")", "[", "]", "{", "}"],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim(' ', '/', '\\', '"'))
                .Where(w => !string.IsNullOrEmpty(w))
                .ToList();

            if (nameLists.Count <= 1)
            {
                title = RawName;
                return false;
            }

            var normalNameList = new List<string>();
            foreach (var name in nameLists)
            {
                var lowerName = name.ToLower();
                if ((name.Equals(name, StringComparison.CurrentCultureIgnoreCase)
                        && name != "FPS"
                        && name != "HUD")
                    || _IsModLoaderMarker(lowerName))
                    subtitleList.Add(name);
                else
                    normalNameList.Add(name);
            }

            if (normalNameList.Count == 0 || subtitleList.Count == 0)
            {
                title = RawName;
                return false;
            }

            title = string.Join(" - ", normalNameList);
            return true;
        }

        private static string _BuildControlSubtitle(
            List<string> subtitleList,
            bool hasModLoaderDescription)
        {
            var finalSubtitles = new List<string>();
            foreach (var rawEx in subtitleList.Distinct())
            {
                var ex = rawEx;
                var lowerEx = ex.ToLower();
                var isModLoader = lowerEx.Contains("forge")
                    || lowerEx.Contains("fabric")
                    || lowerEx.Contains("quilt");

                if (!hasModLoaderDescription && isModLoader) continue;
                if (ex.Length < 16 && lowerEx.Contains("fabric") && lowerEx.Contains("forge"))
                    continue;

                if (isModLoader
                    && !ex.Contains('版')
                    && lowerEx
                        .Replace("forge", "")
                        .Replace("fabric", "")
                        .Replace("quilt", "")
                        .Length
                    <= 3)
                    ex = ex
                            .Replace("Edition", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("edition", "", StringComparison.OrdinalIgnoreCase)
                            .Trim()
                            .Capitalize()
                        + Lang.Text("Download.Comp.Detail.CompItem.EditionSuffix");

                ex = ex
                    .Replace("forge", "Forge")
                    .Replace("neo", "Neo")
                    .Replace("fabric", "Fabric")
                    .Replace("quilt", "Quilt");
                finalSubtitles.Add(ex.Trim());
            }

            return finalSubtitles.Count != 0
                ? $"  |  {string.Join("  |  ", finalSubtitles)}"
                : "";
        }

        private static bool _IsModLoaderMarker(string input)
        {
            return (input.Contains("forge") || input.Contains("fabric") || input.Contains("quilt"))
                && !input
                    .Replace("forge", "")
                    .Replace("fabric", "")
                    .Replace("quilt", "")
                    .RegexCheck("[a-z]+");
        }
    }

    /// <summary>
    ///     剪贴板内容记录：PCL-KSP 已移除剪贴板工程链接监听，仅保留 UI 侧写入的当前文本。
    /// </summary>
    public class CompClipboard
    {
        public static string? currentText;
    }
}
