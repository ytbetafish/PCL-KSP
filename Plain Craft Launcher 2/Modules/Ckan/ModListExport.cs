using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pcl.Ckan;

namespace PCL
{
    /// <summary>
    /// 已安装模组清单的文本 / CSV 格式化（供页面导出按钮与自测共用，避免逻辑只活在点击事件里）。
    /// </summary>
    public static class ModListExport
    {
        public const string CsvHeader = "名称,Identifier,版本,作者,下载量,发布日期";

        /// <summary>按显示名排序，保证同一次导出内容稳定可对比。</summary>
        public static List<ModSearchResult> SortRows(IEnumerable<ModSearchResult> rows)
        {
            return rows.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string BuildCsv(IEnumerable<ModSearchResult> rows)
        {
            var sorted = SortRows(rows);
            var lines = new List<string> { CsvHeader };
            lines.AddRange(sorted.Select(m =>
                $"{Csv(m.Name)},{Csv(m.Identifier)},{Csv(m.Version)},{Csv(m.Author)},{m.Downloads},{m.ReleaseDate:yyyy-MM-dd}"));
            return string.Join("\r\n", lines);
        }

        public static string BuildText(IEnumerable<ModSearchResult> rows)
        {
            var sorted = SortRows(rows);
            return string.Join("\r\n", sorted.Select(m =>
                $"{m.Name} | {m.Version} | {m.Author} | {m.Identifier}"));
        }

        /// <summary>RFC4180 口径的字段转义：含逗号 / 引号 / 换行时整体加引号，内部引号翻倍。</summary>
        public static string Csv(string? value)
        {
            var v = value ?? "";
            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
