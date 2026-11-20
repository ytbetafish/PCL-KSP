namespace Pcl.Ckan;

/// <summary>
/// A detected KSP install on disk.
/// </summary>
/// <param name="RootPath">Absolute path of the KSP root (the folder containing KSP_x64.exe).</param>
/// <param name="Version">Detected KSP version string, or null if it could not be determined.</param>
public sealed record KspInstanceInfo(string RootPath, string? Version);

/// <summary>
/// A row returned by mod search / listing.
/// </summary>
public sealed record ModSearchResult(
    string Identifier,
    string Name,
    string? Description,
    string? Author,
    string? License,
    string? Version,
    string? Homepage,
    bool IsInstalled,
    bool HasUpdate,
    bool IsCompatible = true,
    string? IconUrl = null,
    int Downloads = 0,
    /// <summary>本模组所依赖（作为前置）的模组显示名列表。</summary>
    IReadOnlyList<string>? Depends = null,
    /// <summary>依赖本模组（把它作为前置）的模组显示名列表。</summary>
    IReadOnlyList<string>? ReverseDeps = null,
    /// <summary>与本模组冲突的模组显示名列表。</summary>
    IReadOnlyList<string>? Conflicts = null,
    /// <summary>CKAN 目录登记的模组发布日期；用于按最新/更新排序。</summary>
    DateTime? ReleaseDate = null,
    /// <summary>元数据声明的最高兼容 KSP 版本；null 表示未声明。</summary>
    string? MaxKsp = null);

/// <summary>多提供者依赖中的一个候选模组（CKAN 的「provides」语义）。</summary>
public sealed record ModProviderOption(string Identifier, string Name, string? Version);

/// <summary>
/// 某个依赖关系有多个模组可以满足（TooManyModsProvide），需要用户选择其中要安装的一个。
/// </summary>
/// <param name="Requested">被请求的依赖描述（模块名或 provides 名）。</param>
/// <param name="Requester">提出该依赖的模组显示名。</param>
/// <param name="ChoiceHelpText">元数据里为这个选择提供的说明文本。</param>
public sealed record ModProviderChoice(
    string Requested,
    string? Requester,
    string? ChoiceHelpText,
    IReadOnlyList<ModProviderOption> Options);

/// <summary>
/// The install/upgrade/remove plan computed by CKAN's dependency resolver.
/// When <see cref="FailureReason"/> is non-null the plan could not be produced; the lists are empty.
/// </summary>
public sealed record ModInstallPlan(
    string Target,
    IReadOnlyList<string> ToInstall,
    IReadOnlyList<string> ToUpgrade,
    IReadOnlyList<string> ToRemove,
    string? FailureReason = null,
    /// <summary>本次计划是否允许安装与当前 KSP 版本不兼容的模组。</summary>
    bool AllowIncompatible = false,
    /// <summary>非空表示解析因多提供者依赖中断，需用户选择候选后带选择重新解析。</summary>
    IReadOnlyList<ModProviderChoice>? ProviderChoices = null);
/// <summary>
/// A single published version of a catalogued mod.
/// </summary>
public sealed record ModVersionInfo(
    string Identifier,
    string Version,
    string? ReleaseDate,
    IReadOnlyList<string> DownloadUrls,
    IReadOnlyList<string> GameVersions,
    bool IsCompatible);
