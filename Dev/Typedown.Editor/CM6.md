# CodeMirror 6 迁移 — 工作记录

分支 `cm6-migration`。**只影响共享编辑器与 Uno；未经许可不碰 Windows 版任何东西。**

## 构建（不覆盖 Windows Statics）

默认构建仍输出到 Windows 的 `Statics/`。迁移期用环境变量重定向：

```bash
cd Dev/Typedown.Editor
CM6_BUILD_OUT=<某个独立目录> GENERATE_SOURCEMAP=false NODE_OPTIONS=--openssl-legacy-provider \
  node_modules/.bin/react-app-rewired build
```

产物再用 `Typedown-Uno/Tools/sync-editor.sh <该目录>` 同步给 Uno。Windows 的 Statics 全程不动。

## M0 基线（Muya，300k 字符文档，headless Chrome）

逐键自耗时（ms，EditorBench `bench.js doc300k.md`）：

| 函数 | ms/键 | 说明 |
|---|---|---|
| `getMarkdownAndCursor`（整篇→Markdown 序列化） | 5.54 | 每次都把整篇序列化，随文档增大 |
| `inputHandler` | 1.54 | |
| `getWordCount` | 1.52 | 整篇 |
| `getTOC` | 1.11 | 整篇 |

- 每键发往宿主 `MarkdownChange` ≈ 9KB（300k 文档）。
- 粘贴 5k 字符：56 ms。

**Muya 的特征**：逐键成本随文档大小增长（整篇解析/序列化/统计）。CM6 目标：逐键成本近似常数，与文档大小无关。

## 宿主消息协议（任何替代内核必须遵守）

编辑器 **发给宿主**：`FileLoaded` `MarkdownChange` `StateChange`(含 toc/wordCount) `CursorChange` `OnScroll`(带 loadId) `OutlineCurrent` `ContentFlushed`

编辑器 **接收自宿主**：`LoadFile` `SetMarkdown` `FlushContent` `SettingsChanged` `ThemeChanged` `LanguageChanged` `Search` `Replace` `Find` `SearchOpenChange` `ScrollTo` `RestoreScroll` `OnScroll` `SelectAll` `Copy` `Cut` `Paste` `Duplicate` `DeleteSelection` `DeleteParagraph` `InsertParagraph` `InsertTable` `InsertImage` `Format` `UpdateParagraph` `Export` `ImportFile`

CM6 内核要逐条对齐这些消息名与字段形状，宿主（Windows/Uno）才能不用改。

## 进度

- [x] **M0** 分支、Linux 可构建、输出可重定向、CM6 依赖、基线、协议契约
- [x] **M1** 源码模式换 CM6（替 CM5）— headless 验证（300k 文档，源码模式）：

  | 指标 | CM5 旧 | CM6 新 |
  |---|---|---|
  | 渲染的行 DOM | 22705 | 60 |
  | DOM 节点总数 | 135078 | 225 |
  | 加载 | 4571 ms | 1156 ms |
  | 逐键中位 | 513 ms | 67 ms |

  只渲染视口内约 60 行,加载 4x、逐键 7.6x 更快。`MarkdownChange` 每条仍约 11.6KB（整篇发送，属宿主协议，M5 再优化）。新增 `components/CodeMirror6`，Editor 源码模式切到它；config-overrides 加 `fullySpecified:false` 让 Lezer 的 ESM 能被 webpack5 解析。**nuc/WebKitGTK 冒烟通过**：行号、语法高亮、换行、输入、大纲、字数、深色主题都正常（截图存档）。
- [x] **M2** Live Preview 基础设施 — Lezer 树→装饰管线打通，三档模式（源码/实时/阅读），光标进出露源码（atomicRanges 防卡）。内联元素首批：粗体/斜体/行内代码/链接/标题，标记隐藏、样式套用。headless 验证：`**粗体**`→粗体、反引号/URL 隐藏、光标所在行露源码。Editor 的所见即所得也切到 CM6（Muya 暂留待 M6 移除）。
- [x] **M3** 内联元素 — 删除线（GFM）、`==高亮==`（正则）、图片（`<img>` widget）、行内公式 `$...$`（KaTeX widget）。headless 验证：四者都渲染成成品，`~~`/`==`/`$`/图片 URL 全部隐藏，光标进入露源码。
- [~] **M4** 块级元素（进行中）
  - [x] M4a：引用（左边框，隐藏 `>`）、分割线（`<hr>`）、围栏代码块（隐藏围栏、代码底色）、任务清单复选框。headless 验证通过。
  - [ ] M4b：表格、mermaid、块级公式 `$$`
- [ ] M5 外围能力（TOC/大纲/查找/撤销/滚动）
- [ ] M6 移除 Muya + 回归
