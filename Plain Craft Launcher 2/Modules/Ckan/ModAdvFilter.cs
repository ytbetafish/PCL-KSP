using System;
using System.Collections.Generic;
using System.Linq;
using Pcl.Ckan;

namespace PCL
{
    /// <summary>
    /// CKAN 高级检索条件的纯过滤逻辑（作者 / 描述 / 许可证 / 依赖）。
    /// 依赖过滤的反向依赖表由调用方注入，便于页面与自测共用同一份口径。
    /// </summary>
    internal static class ModAdvFilter
    {
        public static List<ModSearchResult> Apply(
            IEnumerable<ModSearchResult> source,
            ModSearchFilter adv,
            Func<string, IReadOnlyList<string>> reverseDepsLookup)
        {
            var query = source.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(adv.Author))
            {
                query = query.Where(m => m.Author?.Contains(adv.Author, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (!string.IsNullOrWhiteSpace(adv.Description))
            {
                query = query.Where(m => m.Description?.Contains(adv.Description, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (!string.IsNullOrWhiteSpace(adv.License))
            {
                query = query.Where(m => m.License?.Contains(adv.License, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (!string.IsNullOrWhiteSpace(adv.DependsOn))
            {
                IReadOnlyList<string> dependents;
                try
                {
                    dependents = reverseDepsLookup(adv.DependsOn.Trim());
                }
                catch
                {
                    dependents = Array.Empty<string>();
                }
                var names = dependents.ToHashSet(StringComparer.OrdinalIgnoreCase);
                query = query.Where(m => names.Contains(m.Name) || names.Contains(m.Identifier));
            }
            return query.ToList();
        }
    }
}
