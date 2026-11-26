using System.Text;
using System.Text.Json.Nodes;

namespace PCL.Translation;

/// <summary>
///     免注册的免费翻译实现：优先腾讯交互翻译（transmart.imt 的 auto_translation），
///     失败时回退 MyMemory 公共接口。两者均不需要 API 密钥，国内网络可直接访问。
///     这是当前唯一可用的翻译实现（AI 翻译已按需求移除），不需要任何 API 密钥。
/// </summary>
public class FreeTranslationService : ITranslationService
{
    private const string TencentUrl = "https://transmart.qq.com/api/imt";
    private const string MyMemoryUrl = "https://api.mymemory.translated.net/get";

    /// <summary>单次请求的字符上限（MyMemory 免费额度按字符计，这里留出余量）。</summary>
    private const int MaxChunkLength = 480;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/110.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly string ClientKey =
        "browser-chrome-110.0.0-Windows-10-" + Guid.NewGuid();

    public async Task<string?> TranslateAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var target = NormalizeTargetLang(targetLang);
        var chunks = SplitChunks(text.Trim(), MaxChunkLength);
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                continue;
            string? piece = await TryTencentAsync(chunk, target).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(piece))
                piece = await TryMyMemoryAsync(chunk, target).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(piece))
                return sb.Length > 0 ? sb.ToString().Trim() : null;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(piece!.Trim());
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    #region 腾讯交互翻译

    private static async Task<string?> TryTencentAsync(string text, string target)
    {
        // 腾讯免费接口对繁体目标的支持不稳定，繁体请求直接交给 MyMemory，免得把繁体请求翻成简体。
        if (target == "zh-TW") return null;
        var tencentTarget = target == "zh-CN" ? "zh" : target;
        var payload = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["fn"] = "auto_translation",
                ["client_key"] = ClientKey
            },
            ["type"] = "plain",
            ["model_category"] = "normal",
            ["text_domain"] = "",
            ["source"] = new JsonObject
            {
                ["lang"] = "auto",
                ["text_list"] = new JsonArray(text)
            },
            ["target"] = new JsonObject { ["lang"] = tencentTarget }
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TencentUrl)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Referer", "https://transmart.qq.com/");
            req.Headers.TryAddWithoutValidation("Origin", "https://transmart.qq.com");
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = JsonNode.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (body?["header"]?["ret_code"]?.GetValue<string>() != "succ")
                return null;
            var arr = body?["auto_translation"] as JsonArray;
            var first = arr is { Count: > 0 } ? arr[0]?.GetValue<string>() : null;
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }
        catch (Exception ex)
        {
            PCL.Core.Logging.Swallowed.Report(ex);
            return null;
        }
    }

    #endregion

    #region MyMemory 兜底

    private static async Task<string?> TryMyMemoryAsync(string text, string target)
    {
        var pair = DetectSourceLang(text) + "|" + target;
        var url = MyMemoryUrl +
                  "?q=" + Uri.EscapeDataString(text) +
                  "&langpair=" + Uri.EscapeDataString(pair);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.TryAddWithoutValidation("Referer", "https://mymemory.translated.net/");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = JsonNode.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            // MyMemory 有时把 responseStatus 返回成字符串（"200" / "403"），直接 GetValue<int>() 会抛异常，
            // 这里两种类型都接受，避免正常响应被误判成失败。
            if (!TryReadInt(body?["responseStatus"], out var status) || status != 200)
                return null;
            var translated = body?["responseData"]?["translatedText"];
            var translatedText = translated is JsonValue ? translated.GetValue<string>() : null;
            return string.IsNullOrWhiteSpace(translatedText) ? null : translatedText;
        }
        catch (Exception ex)
        {
            PCL.Core.Logging.Swallowed.Report(ex);
            return null;
        }
    }

    /// <summary>容错读取 JSON 整数：既接受数字节点，也接受字符串形式的数字（MyMemory 两种都会返回）。</summary>
    private static bool TryReadInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            return v.TryGetValue<int>(out value);
        if (v.GetValueKind() == System.Text.Json.JsonValueKind.String)
            return int.TryParse(v.GetValue<string>(), out value);
        return false;
    }

    /// <summary>粗判源语言：含 CJK 字符视为中文，否则英文。</summary>
    private static string DetectSourceLang(string text)
    {
        foreach (var ch in text)
            if (ch >= '\u4e00' && ch <= '\u9fff')
                return "zh-CN";
        return "en";
    }

    #endregion

    #region 语言与分段

    private static string NormalizeTargetLang(string? lang)
    {
        var l = (lang ?? "zh-CN").Trim().ToLowerInvariant();
        return l switch
        {
            "zh" or "zh-cn" or "zh-hans" => "zh-CN",
            "zh-tw" or "zh-hant" or "zh-hk" => "zh-TW",
            "en" or "en-us" or "en-gb" => "en",
            "ja" or "jp" => "ja",
            "ko" or "kr" => "ko",
            "ru" => "ru",
            "fr" => "fr",
            "de" => "de",
            "es" => "es",
            "pt" => "pt",
            "it" => "it",
            _ => "zh-CN"
        };
    }

    private static List<string> SplitChunks(string text, int max)
    {
        var result = new List<string>();
        var rest = text.Trim();
        while (rest.Length > max)
        {
            // 每一块都必须不超过上限（MyMemory 免费额度按 500 字符卡，超了就整块失败）。
            // 优先在块内的句末标点处断开；没有再退到词间空格；都没有才硬切。
            var cut = -1;
            for (var i = max - 1; i >= 0; i--)
            {
                if (!IsSentenceEnd(rest[i])) continue;
                cut = i + 1;
                break;
            }
            if (cut <= 0)
            {
                var space = rest.LastIndexOf(' ', max - 1);
                cut = space > 0 ? space : max;
            }
            result.Add(rest[..cut].Trim());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0)
            result.Add(rest);
        return result.Count == 0 ? new List<string> { text } : result;
    }

    private static bool IsSentenceEnd(char ch) =>
        ch is '.' or '!' or '?' or ';' or '。' or '！' or '？' or '；' or '\n';

    #endregion
}
