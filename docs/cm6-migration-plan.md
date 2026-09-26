# 编辑器内核迁移计划：Muya + CodeMirror 5 → CodeMirror 6

起草 2026-09-26。基线：Windows 版 1.2.27 / Uno 版 1.1.3（迁移前最后一版）。

## 目标

用 CodeMirror 6 取代 Muya（所见即所得内核）和 CodeMirror 5（源码模式/代码块），把**源码 / 实时 / 阅读**三种模式合并成同一个引擎的三档状态。收益：

- **性能**：CM6 视口虚拟化，大文档只渲染可见行，摆脱 Muya「整篇解析 + snabbdom 整篇重绘」的瓶颈。
- **少一类 bug**：CM6 的模型本身就是 Markdown 文本，没有 Muya 的 DOM↔Markdown 有损往返（「保存改变文档形状」的根源）。
- **统一**：不再是「Muya 一套 + CM5 一套」两个引擎并存。

## 总策略

1. **全部改动在共享编辑器 `Dev/Typedown.Editor`**。构建产物 `Statics/` 两版共用，Windows 直接用，Uno 靠 `sync-editor.sh` 拷过去。一次改动，两个平台同时受益——**不存在「先迁 Uno 再迁 Windows」两次迁移**。
2. **保持宿主消息协议不变**，让两个 C# 宿主基本不用动。CM6 要继续收发同一组消息：`GetSettings` / `GetCurrentTheme` / `GetStringResources`（握手）、`LoadFile`、`MarkdownChange`、`StateChange`（含 `toc`/`wordCount`）、`OnScroll`（带 `loadId`）、`OutlineCurrent`、`CursorChange`、`FlushContent`…
3. **验证顺序：先 nuc（WebKitGTK）后 hp（WebView2）**。WebKitGTK 是三个后端里能力最弱、最挑剔的，坑先在这里暴露；WebView2 是能力超集，nuc 上跑通的东西它基本白得。这是「先 Uno 后 Windows」这个顺序真正的价值——是验证顺序，不是两次迁移。
4. **增量并存，最后移除 Muya**。每个里程碑都能独立验证，不一次吃成胖子。

## 里程碑

### M0 — 准备与基线
- 建 CM6 骨架：`@codemirror/state` `view` `commands` `language` `lang-markdown` `@lezer/markdown`。
- 用 `Tools/EditorBench` 记录 Muya 现状的逐键耗时和大文档基线，作为回归门槛和对比对象。
- 固化协议契约清单：CM6 要发/收的每条消息，逐条列出字段形状（对照现有 `Dev/Typedown.Editor/src/components/Editor/index.tsx` 与宿主 `Typedown.Core/ViewModels/EditorViewModel.cs`、`Typedown.Uno/MainPage.xaml.cs`）。

### M1 — 源码模式换 CM6（低风险，先落地）
- 用 CM6 + `lang-markdown` 替掉 CM5 的源码模式：语法高亮、查找、缩进、行为。
- 大文件源码模式立刻受益（这是 CM6 的老本行，几乎零成本）。
- 验证：nuc 上大文档源码模式流畅；源码模式下的 `MarkdownChange`/光标/滚动协议对齐。
- 产出：CM5 在源码模式退场（代码块的 CM5 暂留，M4 再迁）。

### M2 — Live Preview 基础设施
- 搭「Lezer 语法树 → Decoration」管线：`ViewPlugin` 遍历可见区语法树，产出 `Decoration.replace` / `Decoration.widget`。
- 光标进出机制：光标所在行/块露出原始 Markdown，其余渲染成成品（Obsidian Live Preview 式）。
- 三档状态开关：**装饰关 = 源码**、**装饰开 = 实时编辑**、**装饰全开 + 只读 = 阅读**。
- 先用最简单的内联元素（粗体/斜体/行内代码/链接）把整条管线打通。

### M3 — 内联元素全覆盖
- 粗体、斜体、删除线、行内代码、链接、图片、行内公式（KaTeX widget）、`==高亮==`、脚注引用、内联 HTML。
- 每种都做三件事：渲染成 widget + 光标落上时露源码 + 编辑回写文本。
- 验证：往返无损（存进去 = 源码），对照 `Tools/EditorBench/spec-check.js`。

### M4 — 块级元素（最大的一块）
- 标题、列表（有序/无序/任务）、引用、分割线、front matter。
- 代码块：Prism 高亮可复用；表格：渲染 + 编辑；数学块（KaTeX）；mermaid 图。
- 表格 / mermaid / 公式作为 atomic widget，点进去编辑或弹出编辑器。表格的网格式富交互最费工。

### M5 — 编辑器外围能力（让宿主几乎不用改）
- 目录 TOC 从 Lezer 树生成 → `StateChange`；当前标题 → `OutlineCurrent`；字数统计。
- 滚动上报 `OnScroll`（带 `loadId`）；查找替换用 CM6 `search`；撤销/重做用 CM6 `history`。
- 粘贴/拖入图片、快捷键转发。逐条对齐协议——这一步做到位，两个宿主基本零改动。

### M6 — 移除 Muya + 回归
- 删除 Muya 及 snabbdom 依赖，包体积下降。
- 全量回归：EditorBench 大文档逐键耗时对比 M0 基线（应显著改善）；`spec-check` 往返；两平台（nuc/hp）手动过所有模式和元素。
- 宿主胶水收尾：Windows 源码模式的模式切换逻辑简化、C# 查找替换 UI 与 CM6 搜索对接；Uno 同理（Uno 的查找栏在网页里，受影响更小）。

## 迁移后各平台的剩余工作量

编辑器是共享的，协议保持不变的前提下：

- **Windows**：接近零的新建工作，主要是回归验证。可能要动的宿主胶水：源码模式切换逻辑（三档统一后简化）、C# 查找替换与 CM6 搜索对接、导出 PDF（走 CDP 读渲染后页面，与内核无关，基本不动）。
- **Uno**：同理，且大多数兼容坑在 nuc/WebKitGTK 阶段已先解决。

## 风险与坑

- **Live Preview 是大头**，开源界没有和 Typedown 功能齐平的现成 CM6 包，得自建或大改。
- **WebKitGTK 的 contentEditable / 装饰时序差异**：每个里程碑先在 nuc 验。
- **手感变化**：从 Muya 的「真·所见即所得（富文本）」变成「源码 + 装饰」（Obsidian 式），这是**产品决定，要先拍板**再动手。
- **表格富交互**最费工，可作为可延后项。

## 验证纪律

每个里程碑：先 nuc（WebKitGTK）验，再 hp（WebView2）回归；性能用 EditorBench 对 M0 基线；往返无损用 spec-check。
