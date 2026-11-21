using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Pcl.Ckan;

/// <summary>SpaceDock 模组详情（仅抽取启动器需要的字段）。</summary>
public sealed record SpaceDockMod(int Id, string Author, string? Background, int Downloads,
    string? ShortDescription, string? License, string? Website, string? LatestVersion);

/// <summary>
/// SpaceDock.info 只读客户端：按 CKAN 模块记录的 spacedock 链接补下载量、作者与横幅图。
/// 网络失败或模组不存在时降级为 null，不阻断 CKAN 注册表主流程；结果按 id 内存缓存。
/// </summary>
public sealed class SpaceDockClient
{
    private const string BaseUrl = "https://spacedock.info";

    /// <summary>6 秒超时：SpaceDock 偶发慢响应时不至于把整页回填拖住（HttpClient 默认 100 秒）。</summary>
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestHeaders = { UserAgent = { ProductInfoHeaderValue.Parse("Pcl.Ckan/1.0") } }
    };

    private readonly ConcurrentDictionary<int, SpaceDockMod?> _cache = new();
    /// <summary>同一 id 的进行中请求：并发调用复用一个 Task，避免列表快速滚动时把 SpaceDock 打到 429。</summary>
    private readonly ConcurrentDictionary<int, Task<SpaceDockMod?>> _inflight = new();

    private sealed class ApiMod
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("background")] public string? Background { get; set; }
        [JsonPropertyName("downloads")] public int Downloads { get; set; }
        [JsonPropertyName("short_description")] public string? ShortDescription { get; set; }
        [JsonPropertyName("license")] public string? License { get; set; }
        [JsonPropertyName("website")] public string? Website { get; set; }
        [JsonPropertyName("source_code")] public string? SourceCode { get; set; }
        [JsonPropertyName("versions")] public List<ApiVersion> Versions { get; set; } = new();

        public sealed class ApiVersion
        {
            [JsonPropertyName("friendly_version")] public string? FriendlyVersion { get; set; }
        }
    }

    /// <summary>从 SpaceDock 链接（/mod/&lt;id&gt;/&lt;slug&gt;）解析模组 id。</summary>
    public static bool TryParseModId(Uri? url, out int id)
    {
        id = 0;
        if (url is null)
            return false;
        var match = Regex.Match(url.AbsolutePath, @"/mod/(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out id);
    }

    /// <summary>拉取模组详情；失败或不存在时返回 null。</summary>
    public async Task<SpaceDockMod?> FetchAsync(int modId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(modId, out var cached))
            return cached;

        // 并发击穿保护：同一个 id 同时只发一次请求，其余调用等这一次的结果。
        var task = _inflight.GetOrAdd(modId, id => FetchCoreAsync(id));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 请求结束后把共享任务移出表：永久结果已写入 _cache；超时/5xx 等临时失败留给下次重试。
            if (_inflight.TryGetValue(modId, out var current) && ReferenceEquals(current, task))
                _inflight.TryRemove(modId, out _);
        }
    }

    private async Task<SpaceDockMod?> FetchCoreAsync(int modId)
    {
        ApiMod? api;
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl}/api/mod/{modId}", CancellationToken.None)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // 只有 404 才是"该模组在 SpaceDock 上不存在"，可永久记为 null；
                // 5xx / 403 / 429 多半是临时故障，不写缓存，下次进页面仍会重试。
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    _cache[modId] = null;
                }
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(CancellationToken.None)
                .ConfigureAwait(false);
            api = await JsonSerializer.DeserializeAsync<ApiMod>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // 网络异常 / 超时同样不写缓存：此前把失败永久缓存，断网一次后整个进程都拿不到图标。
            return null;
        }

        var latestVersion = api?.Versions?.FirstOrDefault()?.FriendlyVersion;
        var website = string.IsNullOrWhiteSpace(api?.Website) ? api?.SourceCode : api?.Website;
        var result = api is null
            ? null
            : new SpaceDockMod(api.Id, api.Author ?? string.Empty, api.Background, api.Downloads,
                api.ShortDescription, api.License, website, latestVersion);
        _cache[modId] = result;
        return result;
    }
}