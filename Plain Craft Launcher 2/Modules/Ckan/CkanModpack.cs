using System;
using System.Collections.Generic;
using System.Linq;
using CKAN;
using Newtonsoft.Json.Linq;
using Pcl.Ckan;

namespace PCL;

/// <summary>
/// CKAN 标准 metapackage（.ckan）的读写：导出时用 CKAN 自身的 CkanModule 校验并序列化，
/// 保证生成的文件符合 CKAN 规范，可被 CKAN 客户端与其他启动器直接读取。
/// </summary>
internal static class CkanModpack
{
    /// <summary>导出用的模组清单标识（.ckan 文件名默认同名）。</summary>
    public const string DefaultIdentifier = "ksp-modlist";

    /// <summary>
    /// 生成 metapackage JSON。depends 列出全部已装模组的标识，
    /// 由 <see cref="CkanModule.FromJson(string)"/> 做规范校验后再输出，字段不合规会抛异常。
    /// </summary>
    public static string Build(IReadOnlyList<ModSearchResult> installed, string? kspVersion, string identifier)
    {
        var depends = installed
            .Where(m => m.IsInstalled && !m.IsAutodetected && !string.IsNullOrWhiteSpace(m.Identifier))
            .Select(m => new JObject { ["name"] = m.Identifier })
            .ToList();

        var root = new JObject
        {
            ["identifier"] = identifier,
            ["name"] = "KSP 模组清单",
            ["abstract"] = "由 PCL KSP 导出的已装模组清单（CKAN metapackage）",
            ["author"] = "PCL KSP",
            ["license"] = "unknown",
            ["version"] = "1.0.0",
            ["kind"] = "metapackage",
            ["depends"] = new JArray(depends)
        };
        if (!string.IsNullOrWhiteSpace(kspVersion))
        {
            root["ksp_version_min"] = kspVersion;
        }

        // spec_version 必须是我们真正需要的最低版本，且必须严格小于本机 CKAN 的
        // Meta.SpecVersion —— CKAN 的 IsSpecSupported 用 IsGreaterThan 判定，
        // 直接写 Meta.SpecVersion 会被自己判为"需要更高版本 CKAN"（UnsupportedKraken）。
        root["spec_version"] = SpecVersionAnalyzer.MinimumSpecVersion(root).ToString();

        // 用 CKAN 自己的解析器校验必需字段，确保其他 CKAN 客户端可读。
        var module = CkanModule.FromJson(root.ToString(Newtonsoft.Json.Formatting.Indented));
        return module.ToJson();
    }

    /// <summary>
    /// 从 .ckan / 清单 JSON 里提取要安装的模组标识。支持三种输入：
    /// 标准 metapackage（读 depends）、CKAN 的标识数组、以及本程序早期的 {name, version} 数组。
    /// </summary>
    public static IReadOnlyList<string> Parse(string json)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddId(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            // 形如 "ident=version" 的旧写法只保留标识
            var eq = text.IndexOf('=');
            if (eq > 0)
            {
                text = text.Substring(0, eq).Trim();
            }
            if (text.Length > 0 && seen.Add(text))
            {
                ids.Add(text);
            }
        }

        var token = JToken.Parse(json);
        switch (token)
        {
            case JArray array:
                foreach (var item in array)
                {
                    if (item.Type == JTokenType.String)
                    {
                        AddId(item.ToString());
                    }
                    else if (item is JObject row)
                    {
                        AddId(row["identifier"]?.ToString() ?? row["name"]?.ToString());
                    }
                }
                break;

            case JObject obj:
                if (obj["depends"] is JArray { Count: > 0 } depends)
                {
                    foreach (var dep in depends)
                    {
                        if (dep.Type == JTokenType.String)
                        {
                            AddId(dep.ToString());
                        }
                        else if (dep is JObject descriptor)
                        {
                            AddId(descriptor["name"]?.ToString());
                        }
                    }
                }
                else
                {
                    // 单模组 .ckan：直接按 identifier 安装
                    AddId(obj["identifier"]?.ToString() ?? obj["name"]?.ToString());
                }
                break;
        }

        return ids;
    }
}
