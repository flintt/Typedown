# 本机自动化

Typedown 可以让**本机上以你的身份运行的程序**读取和编辑已经打开的文档：脚本批量改一处措辞、AI 助手在你看着的时候改稿、测试工具驱动编辑器。它默认关闭。

## 打开和关闭

**设置 → 通用 → 允许本机自动化**。关闭时接口不存在；打开后才开始监听。再次关闭会立即断开所有已连接的程序。

打开后，只要有程序连着，窗口标题末尾会显示“自动化已连接”；程序每改一次文档，标题会短暂显示“{程序名} 编辑了 {文档}”。被改动的文字会淡出高亮约 2 秒，可以在 **设置 → 通用 → 高亮程序所做的更改** 里关掉高亮，但标题提示不能关，程序也不能替你关。

## 能做什么，不能做什么

程序可以：

- 列出打开的窗口和文档，读取正文（包括你刚输入、还没保存的内容）和标题结构；
- 打开文件、新建文档、切到某个文档；
- 按文本替换或整篇替换正文，撤销、重做，保存到文档原有的路径；
- 读写四个外观设置：主题、字号、行高、文字方向。

程序不能：

- 选择保存位置（没有“另存为”），关闭文档或窗口；
- 读写其他设置，包括自定义 CSS、这两个自动化开关本身；
- 通过网络访问：接口只在本机，Windows 上是只允许你这个用户打开的命名管道，Linux 上是只有你能进入的目录里的 socket（权限 0600）。

每次写入都要带上程序读到的**版本号**。你在程序读取之后又输入了内容，它的写入会被拒绝，它必须重新读取；不会有旧内容覆盖你刚写的字。可视编辑器可能改写格式的正文（例如 setext 标题）默认也会被拒绝，除非程序明确表示接受；可视编辑会丢失内容的正文（例如部分原始 HTML）一律拒绝。

## 安全边界

这个开关信任的是**你这个系统用户下运行的所有程序**：打开后，任何以你的身份运行的程序都能连上并改你打开的文档。它没有逐个程序的授权，也不是插件沙箱。只在你信任本机上运行的软件时打开；不用时关掉。

程序连接时自报的名字只用于显示，Typedown 无法核实。

## 命令行：typedownctl

```
typedownctl status
typedownctl documents
typedownctl get <documentId> --latest --text
typedownctl replace-text <documentId> --base-revision 3 --find "旧说法" --replacement "新说法" --expected-count 1 --save
typedownctl replace <documentId> --base-revision 4 --stdin < new.md
```

加 `--json` 时标准输出只有一个 JSON 值，适合脚本。退出码：0 成功，3 Typedown 没运行或开关没开，5 文档不存在，6 版本或匹配次数冲突（重新读取后再试），7 编辑器未就绪，8 保存失败。完整说明见 `typedownctl help`。

## AI 助手：typedown-mcp

`typedown-mcp` 是一个 MCP 服务器，支持 MCP 的 AI 工具（例如 Claude Code）通过它读写你打开的文档。注册方法见 [Tools/Typedown.Mcp/README.md](../Tools/Typedown.Mcp/README.md)。遇到冲突时它会让 AI 重新读取，不会拿旧内容重试。

## 自己写程序

协议是 JSON-RPC 2.0，按 Content-Length 分帧，规格见 [automation-api-spec.md](automation-api-spec.md)，参数和结果的 JSON Schema 见 [automation-schema/v1.json](automation-schema/v1.json)。[automation-examples](automation-examples/) 里有 Python 和 PowerShell 的直接客户端示例。

v1 接口已经冻结：v1 内只会新增方法和可选参数，不会删除、改名或改变已有行为，详见规格第 6 节。

## 目前的限制

- `typedownctl` 和 `typedown-mcp` 还没有打进安装包，需要从源码构建（`dotnet publish Tools/Typedown.Cli`、`dotnet publish Tools/Typedown.Mcp`）。
- 保存、撤销和重做只作用于窗口当前显示的文档；后台标签先用 `document.focus` 切过去。
- Linux 版（Typedown-Uno）的接口在 `automation` 分支，socket 位于 `$XDG_RUNTIME_DIR/typedown/automation.v1.sock`。
