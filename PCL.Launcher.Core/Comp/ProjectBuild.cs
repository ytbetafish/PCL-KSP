using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Core.Utils;

namespace PCL;

/// <summary>
///     由原版 Comp 平台层剥离后保留的自定义 Json 解析分支（PKL / CKAN 适配层使用）。
/// </summary>
public static partial class Comp
{
    public partial class CompProject
    {
        private static CompProjectBuildResult _BuildFromCompJson(JsonObject data)
        {
            var result = new CompProjectBuildResult
            {
                FromCurseForge = (string?)data["DataSource"] == "CurseForge",
                Type = (CompType)(data["Type"]?.ToObject<int>() ?? 0),
                Slug = (string?)data["Slug"] ?? "",
                Id = (string?)data["Id"] ?? "",
                RawName = (string?)data["RawName"] ?? "",
                Description = (string?)data["Description"] ?? "",
                Website = (string?)data["Website"] ?? "",
                DownloadCount = data["DownloadCount"]?.ToObject<int>() ?? 0,
                Tags =
                [
                    .. (data["Tags"] as JsonArray ?? [])
                    .Select(t => t?.ToString() ?? "")
                ]
            };

            if (data.TryGetPropertyValue("CurseForgeFileIds", out var id))
                result.CurseForgeFileIds =
                [
                    .. (id as JsonArray ?? [])
                    .Select(t => t?.ToObject<int>() ?? 0)
                ];

            if (data.TryGetPropertyValue("LastUpdate", out var last))
                result.LastUpdate = last?.ToObject<DateTime>();

            if (data.TryGetPropertyValue("ModLoaders", out var loaders))
                result.ModLoaders =
                [
                    .. (loaders as JsonArray ?? [])
                    .Select(t => (CompLoaderType)(t?.ToObject<int>() ?? 0))
                ];

            if (data.TryGetPropertyValue("LogoUrl", out var url))
                result.LogoUrl = (string?)url;

            if (data.TryGetPropertyValue("Drops", out var drops))
                result.Drops =
                [
                    .. (drops as JsonArray ?? [])
                    .Select(t => t?.ToObject<int>() ?? 0)
                ];

            return result;
        }

        private sealed class CompProjectBuildResult
        {
            public List<int> CurseForgeFileIds = [];
            public string Description = "";
            public int DownloadCount;
            public List<int> Drops = [];
            public bool FromCurseForge;
            public string Id = "";
            public DateTime? LastUpdate;
            public string? LogoUrl;
            public List<CompLoaderType> ModLoaders = [];
            public string RawName = "";
            public string Slug = "";
            public List<string> Tags = [];
            public CompType Type;
            public string Website = "";
        }
    }
}
