<p align="center">
  <img alt="Typedown Logo" src="./logo.png" width="100px" />
  <h1 align="center">Typedown</h1>
</p>

<p align="center">
  <a href="https://github.com/flintt/Typedown/releases/latest"><img alt="最新版本" src="https://img.shields.io/github/v/release/flintt/Typedown?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC"></a>
  <a href="https://github.com/flintt/Typedown/releases"><img alt="下载量" src="https://img.shields.io/github/downloads/flintt/Typedown/total?label=%E4%B8%8B%E8%BD%BD%E9%87%8F"></a>
  <a href="LICENSE"><img alt="许可证" src="https://img.shields.io/github/license/flintt/Typedown?label=%E8%AE%B8%E5%8F%AF%E8%AF%81"></a>
</p>

Windows 平台的所见即所得 Markdown 编辑器，WinUI 界面，编辑内核来自 MarkText 的 Muya。
本仓库是 [byxiaozhi/Typedown](https://github.com/byxiaozhi/Typedown) 的 fork，在上游基础上持续修 bug、加功能（多标签、全文搜索、会话恢复、HedgeDoc 分享、阅读模式……），每个版本的具体改动见 [Releases](https://github.com/flintt/Typedown/releases)。

> A WYSIWYG Markdown editor for Windows (WinUI), forked from [byxiaozhi/Typedown](https://github.com/byxiaozhi/Typedown)
> and developed further: document tabs, folder-wide search, session restore, HedgeDoc sharing, reading mode and more.
> See the [releases](https://github.com/flintt/Typedown/releases) for what each version changed.

## 下载

去 **[最新发布](https://github.com/flintt/Typedown/releases/latest)** 下载，x64 和 ARM64 都有：

| 文件 | 说明 |
|---|---|
| `Typedown-windows-x64-*.exe` / `-arm64-*.exe` | 安装包，一路下一步即可 |
| `Typedown-portable-x64-*.zip` / `-arm64-*.zip` | 便携版，解压即用，不写注册表 |
| `Typedown.Package_*.msix` | MSIX 包；需要先安装同一发布里的 `Typedown-signing-certificate.cer` 到「受信任的根证书颁发机构」 |

跨平台（Linux / macOS）请用 Uno 移植版：**[flintt/Typedown-Uno](https://github.com/flintt/Typedown-Uno/releases/latest)**。

上游作者在微软商店上架的是**上游版本**，不含本仓库的改动：[Microsoft Store](https://apps.microsoft.com/detail/9p8tcw4h2hb4)。

主题可以自己写：一个 CSS 文件放进主题文件夹即可，格式见 **[自定义主题](docs/custom-theme.md)**。

遇到问题请开 [issue](https://github.com/flintt/Typedown/issues/new/choose)，写清楚版本号（设置 → 关于）、复现步骤和日志（`%LOCALAPPDATA%\Typedown\logs\`）。本仓库的 issue 由 AI（Claude Code）分析和修复。

## Screenshots
<figure>
<img src="https://github.com/byxiaozhi/Typedown/assets/31278216/d0c9d76b-ecd2-4941-90ca-0f8c639c2ef0" width=200/>
<img src="https://github.com/byxiaozhi/Typedown/assets/31278216/d5320590-2d0b-4f9a-a3d2-4661eb021758" width=200/>
<img src="https://github.com/byxiaozhi/Typedown/assets/31278216/2ce7795c-1043-41ed-a420-c42f7aa5aa80" width=200/>
<img src="https://github.com/byxiaozhi/Typedown/assets/31278216/a2df17f3-3100-4129-b0ba-0e90e14a89bf" width=200/>
</figure>

## Building from source

### 1. Prerequisites
[Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the following individual components:
  - .NET Core 3.1 SDK
  - Git for Windows

[Node.js](https://nodejs.org/) with the following global packages:
  - [yarn](https://yarnpkg.com/)

### 2. Clone the repository
```ps
git clone https://github.com/flintt/Typedown
```

This will create a local copy of the repository.

### 3. Build the project
First go to the directory `Typedown\Dev\Typedown.Editor` and run `yarn && yarn build`

```ps
cd Typedown\Dev\Typedown.Editor
yarn && yarn build
```
![20240319232236_rec_](https://github.com/byxiaozhi/Typedown/assets/31278216/3f038707-9311-4aad-846b-a22e8bad6857)

After finishing the compilation of `Typedown.Editor`, you can see the generated product in the directory `Typedown\Dev\Typedown\Resources\Statics`.

Then use VisualStudio 2022 to open `Typedown\Typedown.sln`, right-click on the Typedown project and select Set as Startup Project.

In the top pane, select the solution configuration you want to build in, the difference between these configurations is as follows
- Debug: The `Typedown.Editor` will be accessed using the http://localhost:3000 address, to use this configuration you need to also start the Typedown.Editor project using yarn start in the Typedown\Dev\Typedown.Editor directory.
- Debug_Local: The `Typedown.Editor` will be accessed using the compiled product (Typedown\Dev\Typedown\Resources\Statics)
- Release: Used when releasing a project

Then select the platform you want to build on (x64, x86, or arm64) and click Run!

![20240319232529_rec_](https://github.com/byxiaozhi/Typedown/assets/31278216/50ef6e56-b177-49b0-b361-83659d25a40e)

### Contributors
Want to contribute? Open an [issue](https://github.com/flintt/Typedown/issues) describing what you intend to
change before sending a [pull request](https://github.com/flintt/Typedown/pulls). Upstream discussions belong
in [byxiaozhi/Typedown](https://github.com/byxiaozhi/Typedown/issues).

## 许可证

MIT，沿用上游的许可证（见 `LICENSE`）。编辑内核来自 [MarkText](https://github.com/marktext/marktext) 的 Muya（同为 MIT）。
