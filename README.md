# PKL — KSP Launcher

基于 **PCL-CE**（Plain Craft Launcher Community Edition，dev 分支）改造的 **《坎巴拉太空计划》(Kerbal Space Program, KSP)** 启动器。

- 以 PCL-CE 的 WPF 程序骨架为底座，移除 Minecraft 专属的启动、下载与账号功能（少量 MC 兼容代码与素材暂留）；
- 集成 **CKAN**（Comprehensive Kerbal Archive Network）核心，实现 KSP 模组的浏览 / 搜索 / 安装 / 依赖解析；
- 参考 PCL-CE 的"简介翻译"，实现模组简介的中 / 英翻译切换。

技术栈：C# / WPF / .NET 10 (`net10.0-windows`)。

> ## ⚠️ 项目状态：开发中，尚未完成
>
> 功能仍在补齐与返工（翻译测试、GitHub 下载加速、界面细节等），可能出现崩溃、卡死或数据异常。当前仅供自用测试，**不建议作为唯一启动器依赖**，也不建议在重要存档环境下使用。

> ## 声明：本项目不分发原版游戏
>
> PKL 只是一个启动器，**本仓库与任何发行物均不含《坎巴拉太空计划》(Kerbal Space Program) 游戏本体、DLC 或官方资源**。运行前请自备通过合法渠道获得的 KSP 正版游戏；游戏及全部官方内容的版权归其权利人（Squad / Private Division 等）所有。
>
> 模组数据来自 CKAN 公共索引与 SpaceDock，模组本体由各自作者发布，本项目不托管、不分发这些资源；国内 CKAN 加速源由坎喵社区（QQ 群 758050820）群友集资提供，本项目仅接入，不提供该服务。

---


## 一、上游来源

| 组件 | 仓库 | 版本/分支 | 许可 |
|---|---|---|---|
| 改造底座 | `PCL-Community/PCL-CE` | `dev`（commit `e8d2883`） | Apache-2.0（"Plain Craft Launcher 2"目录除外，沿用其原许可） |
| 模组管理核心 | `KSP-CKAN/CKAN` | `master`，对标 `v1.36.4` | MIT |
| 简介翻译原型 | 取自 PCL-CE `PCL.Launcher.Core/Comp/ProjectProps.cs` 的翻译代理 | — | 同 PCL-CE |

> 最初考虑的 `Hex-Dragon/PCL2 v2.13.1.1` 是 VB.NET + .NET Framework 4.8 的旧式工程，与当前技术栈不匹配，故改用已 C# 化、架构更清晰的 PCL-CE dev。

## 二、目录结构

> 本节由 GitHub Trees API 生成（`GET /repos/ytbetafish/PKL/git/trees/main?recursive=1`，截至 `1782731` 共 1 654 个文件），括号内为该目录文件数。结构变动后重新拉取即可，不必手写维护。

```
PKL/
├─ PCL.Core/                     (390)  核心库：日志、IO/网络、配置、平台、加密、基础 UI 设施
├─ PCL.Core.Wpf/                 (251)  WPF 基础：主题资源、控件、动画、图标包（lucide 87 个 SVG）
├─ PCL.Core.SourceGenerators/      (9)  源生成器
├─ PCL.Core.Test/                 (49)  上游单元测试，改造后未再维护
├─ PCL.Launcher.Core/            (175)  业务核心：启动、下载、实例、更新、翻译
├─ Plain Craft Launcher 2/       (279)  WPF 主程序
│   ├─ Pages/                    (100)  页面（见下表）
│   ├─ Controls/                  (61)  PCL 自定义控件
│   ├─ Modules/                   (47)  业务模块（设置、启动器、横幅缓存等）
│   ├─ Images/                    (47)  位图资源（含部分 MC 遗留素材）
│   ├─ Resources/                 (14)  本地化与提示文本
│   └─ FormMain.xaml(.cs)                主窗口外壳与顶栏导航
├─ external/CKAN.Core/           (172)  内联 CKAN 核心（MIT，目标 net10.0）
├─ src/Pkl.Ckan/                   (8)  CKAN 适配层（无头，不依赖 WPF）
├─ docs/                           (2)  改造方案与参考样例分析
├─ .github/                       (12)  issue / PR 模板与 CI（继承自上游，尚未适配）
└─ 根目录                         (11)  slnx / README / LICENSE / CONTRIBUTING / cliff.toml 等
```

`Plain Craft Launcher 2/Pages` 明细：

| 目录 / 文件 | 内容 |
|---|---|
| `PageLaunch` | 启动页：KSP 路径、版本信息、启动按钮 |
| `PageDownload` | 下载页：CKAN 模组浏览、搜索、筛选、排序、卡片横幅 |
| `PageSettings` | 设置页：左栏导航 + 8 个独立右栏页（KSP 实例 / CKAN / 翻译 / 外观 / 语言 / 日志 / 杂项 / 关于） |
| `PageInstance` | 实例设置：概览、模组（`PageInstanceCompResource`）、存档、截图 |
| `PageSelect` | 实例选择 |
| `PageMods` | 实例级模组列表（卡片视图） |
| `PageTools` | 「更多」页：外链合集与说明 |
| `PageSpeedLeft.xaml(.cs)` | 任务管理页（右下角下载按钮进入），配套 `PageSpeedLeftViewModel.cs` |

## 三、构建方式

需要 **.NET 10 SDK**（示例假定装在 `%USERPROFILE%\.dotnet`，若已在 PATH 可省略前两行）。干净构建：

```powershell
# 指向 .NET 10 SDK（若已在 PATH 可省略前两行）
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"
$env:Path="$env:USERPROFILE\.dotnet;$env:Path"
# 可选：把 NuGet 缓存与 CLI 主目录重定向到仓库内，避免权限问题
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'

# 在仓库根目录执行
dotnet build ".\Plain Craft Launcher 2.slnx" -c Release
```

预期结果：**0 error**；warning 多为可空性与未使用字段，不影响运行。
运行主程序：构建输出目录下的 `PKL.exe`（`AssemblyName` 为 `PKL`；未指定 `-o` 时位于 `Plain Craft Launcher 2\bin\Release\<TargetFramework>\`）。

## 四、功能清单

外壳与导航：

- [x] PCL WPF 外壳：主窗口、主题资源、设置 JSON 持久化、日志、下载引擎框架；
- [x] 移除 MC 专属功能：版本隔离、MC 账号/皮肤、Java 检测、Forge/Fabric 安装器、MC Mod 平台、资源包/光影/存档/服务器等页面与模块；
- [x] 四页顶栏导航：启动 / 下载 / 设置 / 更多，左栏滑入动画（`PageChangeAnim`）；
- [x] 设置页：左栏导航 + 8 个独立右栏页（KSP 实例 / CKAN / 翻译 / 外观 / 语言 / 日志 / 杂项 / 关于），左栏只创建一次、右栏保持上次栏目；
- [x] 实例体系：实例选择、实例设置（概览 / 模组 / 存档 / 截图）、实例级模组列表；
- [x] 已去掉：启动时的「PCL CE 版本说明」对话框（`ShowCeAnnounce`）与 DEBUG 版本提示对话框。

模组与下载：

- [x] CKAN 集成：KSP 路径探测、模组索引刷新、搜索、已安装列表、依赖解析安装计划、执行安装；
- [x] 模组管理：卸载 / 更新、禁用启用（改 DLL 后缀，可逆）、收藏、复制名称/链接；
- [x] 列表能力：筛选标签与计数、排序（名称 / 下载量 / 日期）、底部选择栏批量操作（收藏 / 分享 / 卸载 / 更新）；
- [x] 模组详情页：多版本列表、收藏、简介翻译；
- [x] 任务管理页（右下角下载按钮进入）：任务列表、进度与取消；
- [x] 横幅按需加载 + 磁盘缓存（上限 150 MB）；
- [x] CKAN 源：默认启用国内镜像（坎喵公共源），支持多源与三种策略（仅镜像 / 官方优先 / 仅官方）；
- [x] 简介翻译：模组简介「中 / EN」切换，OpenAI 兼容接口（默认 DeepSeek），设置页可配 BaseUrl / ApiKey / Model，内存缓存；
- [x] 启动 KSP 进程。

仍在进行：

- [ ] 翻译服务的「测试连接」按钮；
- [ ] GitHub 下载加速镜像（`ghfast.top` / `gh-proxy.com` / `ghproxy.net` 等）的吞吐测试与接入。

## 五、分层架构

```
┌────────────────────────────────────────┐
│  UI 层（WPF）                          │
│  PageLaunch / PageDownload / PageSelect│
│  PageInstance / PageSettings / PageTools│
│  PageSpeedLeft / 模组详情              │
├────────────────────────────────────────┤
│  适配层（无 WPF 依赖）                 │
│  src/Pkl.Ckan：IModService / ModService │
│  KspInstanceService / SpaceDockClient  │
├────────────────────────────────────────┤
│  核心库                                │
│  external/CKAN.Core（MIT）             │
│  PCL.Core / PCL.Core.Wpf / PCL.Launcher.Core │
└────────────────────────────────────────┘
```

## 六、页面动画说明

左栏切换与右栏内容滑入沿用原版 PCL 的通用实现（设置页、实例设置、更多页共用），以设置页为例：

- **左栏切换动画**：`PageChangeAnim` 方法
  - 旧左栏 `TriggerHideAnimation()` → 延迟 110ms 换 PanMainLeft.Child → 新左栏 `Opacity=0` → 延迟 30ms `TriggerShowAnimation()`
- **右栏内容切换动画**：`MyPageRight.TriggerEnterAnimation`
  - 逐个元素从上面滑入（TranslateY -16 → 0，每个元素延迟 25ms）
- **左栏只创建一次**：用静态变量 `settingsLeft` 保存，避免每次切换都重新创建
- **右栏状态保持**：用静态变量 `settingsNav` 保存当前选中的栏目，再进入设置页时右栏保持上次选中的栏目

## 七、许可

本仓库为二次开发作品：PCL 外壳部分遵循 PCL-CE 相应许可；CKAN 核心遵循 MIT；新增 PKL 代码可按 Apache-2.0 使用。详见各上游 `LICENSE`。

