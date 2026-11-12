# MCP 服务器：typedownctl mcp

`typedownctl mcp` 是一个 MCP 服务器（stdio），让支持 MCP 的 AI 工具（例如 Claude Code、Claude Desktop）读写你在 Typedown 里打开的文档。它走的是本机自动化接口（见 [automation.md](automation.md)），所以先在 **设置 → 通用 → 允许本机自动化** 里打开开关。有程序连着时，窗口标题会显示；AI 每改一次，标题会写明是哪个 AI 改了哪个文档，改动的文字会短暂高亮。

## 注册

Windows 安装版和便携版把 `typedownctl.exe` 放在 `Typedown.exe` 旁边（安装版默认 `C:\Program Files\Typedown\typedownctl.exe`）；MSIX 包（含 Microsoft Store 版）装好后，在终端里直接输入 `typedownctl` 即可，下面的命令把路径换成 `typedownctl` 就行（`claude mcp add typedown -- typedownctl mcp`）<!-- linux -->；Linux 版（deb 包）的命令是 `/usr/bin/typedownctl`<!-- /linux -->。

Claude Code：

```
claude mcp add typedown -- "C:\Program Files\Typedown\typedownctl.exe" mcp
```

<!-- linux -->
Linux：

```
claude mcp add typedown -- typedownctl mcp
```

<!-- /linux -->
其他 MCP 客户端（它们 JSON 配置里的 `mcpServers`<!-- linux -->，Linux 上 `command` 写 `typedownctl`<!-- /linux -->）：

```json
{
  "mcpServers": {
    "typedown": { "command": "C:\\Program Files\\Typedown\\typedownctl.exe", "args": ["mcp"] }
  }
}
```

从源码运行（开发时）：`dotnet publish Tools/Typedown.Cli -f net10.0 -o <dir>`，然后命令是 `dotnet <dir>/typedownctl.dll mcp`。

`--endpoint NAME` 连接另一个端点（自动化测试宿主把它的端点名写在 `automation-endpoint.txt`）。

## 工具

| 工具 | 作用 |
| --- | --- |
| `typedown_list_documents` | 打开的文档：id、窗口、路径、标题、版本号、是否已保存、是否正在显示 |
| `typedown_read_document` | 当前正文（包括还没上报的输入）、版本号、内容哈希；可选标题结构。可只读一部分：`heading`（某个标题的一节，到下一个同级或更高级标题为止，同名时用 `headingOccurrence`）或 `startLine`/`endLine`（从 1 起，含两端），返回 `range`（起止行、总行数）；版本号和哈希仍是整篇的。另返回 `normalizationNote`（规范化状态对下一次写入意味着什么的说明）和 `lastEditThisSession`（本会话上一次写入的文字还在不在：`unchanged`、`present`、`partly`、`gone`、`changed`） |
| `typedown_replace_text` | 把 `find` 换成 `replacement`，只在它在 `baseRevision` 时正好出现 `expectedCount` 次（默认 1）才执行 |
| `typedown_edit_text` | 一次提交多组查找替换：都在读到的同一版正文上计数，每组都要正好出现 `expectedCount` 次、互不重叠，才一起写入，只产生一个版本（撤销和审阅各一次）；任何一组不符就什么都不改，错误里的 `editIndex` 指出是哪一组。挪段落（一处删、一处加）用它，不必整篇替换 |
| `typedown_replace_document` | 替换整篇正文，只在文档仍是 `baseRevision` 时执行；一处修改用 `typedown_replace_text`、几处用 `typedown_edit_text` |
| `typedown_save_document` | 保存到文档原有的文件（未命名文档不行） |
| `typedown_close_document` | 关闭一个已保存的文档；有未保存更改时拒绝，什么都不丢 |
| `typedown_get_view` | 窗口的模式、侧栏、状态栏、专注/打字机模式和屏幕位置大小 |
| `typedown_set_view` | 改变上面几项；模式等是你自己的设置，工具说明要求 AI 只在你要求时改、用完改回去 |

写入可以带 `save`（顺便保存）、`reveal`（把文档切到前台）、`scrollToChange`（切到前台，并把改动处滚进视野，光标不动；你看着它改稿时用）和 `allowFormattingChanges`（接受可视编辑器首次编辑时可能改写格式的正文）。

规范化（`normalization`）：`pendingNormalization` 为 `none` 或 `knownFormatting` 时不用管；`unknown` 表示无法证明可视编辑器会逐字保留这段 Markdown，你第一次在可视模式里编辑时可能被重排格式，`reasons` 说明是哪种（`formatting-changed` 只是列表符号、空行、表格对齐之类的排版，不丢字；`notEvaluated` 是这个标签还没在可视编辑器里显示过），这时写入默认被拒（`normalization_unclassified`），确认只是格式变化可以接受时再传 `allowFormattingChanges: true`；`unsafe` 和 `content_not_roundtrippable` 表示会丢内容（多是原始 HTML），任何选项都不写。

`lastEditThisSession` 是按正文判断的：本会话上次写进去的文字是否还在。`gone` 说明被你在审阅里拒绝、撤销或改掉了。文档开着审阅模式时，它还带 `review`：审阅模式对这次修改（按写入返回的 `operationId`）的结论——`pending`（待审）、`accepted`（已接受）、`rejected`（已拒绝）、`partly`（部分处理）及各自的条数。自动化接口对应的方法是 `document.reviewStatus`（`documentId`，可选 `operationIds`）。AI 不能自己接受或拒绝修订。

失败时返回 `isError: true`，`structuredContent` 是 `{ "error": <接口错误，和 typedownctl --json 输出的完全一样>, "next": "<下一步该怎么做>" }`。遇到 `revision_conflict` 或 `match_count_mismatch` 时什么都没写，AI 必须重新读取；服务器不会拿旧内容重试。
