# 如何为 PCL KSP 做贡献

本仓库是对 [PCL-CE](https://github.com/PCL-Community/PCL-CE) 的二次开发，改造目标是 KSP 启动器，**不再接受与上游同步相关的改动**；与 PCL-CE 本体有关的问题请提交到上游仓库。

- 仓库：`https://github.com/ytbetafish/PCL-KSP`
- 默认分支：`main`
- 构建方式见 [README](./README.md) 第三节

## 报告问题

1. 先在 [Issues](https://github.com/ytbetafish/PCL-KSP/issues) 里搜一下有没有相同的问题；
2. 没有的话新建 Issue（Bug 模板即可），尽量包含：
   - 现象与期望行为；
   - 复现步骤；
   - 启动器版本（设置 → 关于）、KSP 版本；
   - 相关日志片段或截图。

**注意**：贴日志时先把 `%USERPROFILE%\...` 这类本机绝对路径换成占位符，也不要提交 `pclksp-settings.json`、`build_out.txt` 这类本地文件。

## 提交代码

1. Fork 并克隆本仓库：

   ```bash
   git clone https://github.com/<你的用户名>/PCL-KSP.git
   ```

2. 从 `main` 拉分支，分支名建议 `feat/xxx` 或 `fix/issue-123-xxx`：

   ```bash
   git checkout -b feat/your-feat-name
   ```

3. 改完先本地编译，确认 0 错误（命令见 README 第三节）；
4. 提交，提交信息使用 Angular 规范：

   ```bash
   git commit -m "<type>(scope): <subject>"
   ```

5. 推到自己的 fork，向本仓库 `main` 开 PR，并在 PR 描述里写清自测情况。

## 提交信息规范

格式：

```text
<type>(scope?): <subject>

<body>

<footer>
```

`type` 取以下之一：

| type | 用途 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修复 bug |
| `refactor` | 重构（既不加功能也不修 bug） |
| `style` | 格式化、空格等不影响逻辑的改动 |
| `docs` | 文档 |
| `build` | 构建系统或依赖 |
| `ci` | CI 配置 |
| `chore` | 杂项（脚本、工具配置等） |
| `test` | 测试 |
| `pref` | 性能优化 |

- `subject` 用祈使句、现在时，不加句号；
- `body` 说明动机与行为变化；
- 破坏性改动在 `footer` 里以 `BREAKING CHANGE:` 开头说明；
- 一个 PR 尽量只做一件事，修一个功能提交一次。

## UI 相关约定

界面改动优先沿用原版 PCL-CE 的页面与控件实现（`MyPageLeft` / `MyPageRight` / `MyCard` / `MyListItem` 等），不要另起一套自绘界面。
