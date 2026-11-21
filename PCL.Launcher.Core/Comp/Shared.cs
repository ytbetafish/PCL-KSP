using System.Collections.Concurrent;
using System.Text;

namespace PCL;

/// <summary>
///     由原版 Comp 平台层剥离出的共享模型片段：工程缓存与文件名清洗。
/// </summary>
public static partial class Comp
{
    /// <summary>
    ///     已解析工程缓存（Id → 工程模型），供 UI 侧（如依赖折叠）查询。
    /// </summary>
    public static ConcurrentDictionary<string, CompProject> compProjectCache = new();

    /// <summary>
    ///     将文件名中的非法字符替换为全角形式，避免写入失败。
    /// </summary>
    public static string CompFileNameSanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "download";

        var sanitized = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
            sanitized.Append(
                c switch
                {
                    '\\' => '＼',
                    '/' => '／',
                    ':' => '：',
                    '*' => '＊',
                    '?' => '？',
                    '"' => '＂',
                    '<' => '＜',
                    '>' => '＞',
                    '|' => '｜',
                    _ when char.IsControl(c) => '_',
                    _ => c
                });

        var result = sanitized.ToString().Trim();
        return result is "" or "." or ".."
            ? "download"
            : result;
    }
}
