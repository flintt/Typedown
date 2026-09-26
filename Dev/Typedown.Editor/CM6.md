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
- [ ] **M1** 源码模式换 CM6（替 CM5）
- [ ] M2 Live Preview 基础设施
- [ ] M3 内联元素
- [ ] M4 块级元素
- [ ] M5 外围能力（TOC/大纲/查找/撤销/滚动）
- [ ] M6 移除 Muya + 回归
