# 性能与可靠性代码审阅及改进建议

- 日期：2026-09-27
- 审阅基线：`7591897f2d19d720f004faa9d9739ed591bd6627`，当前应用版本 `1.2.27`。
- 范围：Windows 宿主、文档/标签状态、保存与恢复、文件监控、编辑器消息和更新路径、现有测试与 CI。
- 本轮交付为建议文档，不修改业务代码。开始审阅时工作区干净。
- 下文行号对应上述提交；“代码可确认”表示能从实现直接证明缺口，**不表示已经在 Windows 上复现故障**。性能收益需要基准验证，文中不把推算写成实测。

## 结论与优先级

最优先的工作是建立“文档身份、内容修订、保存快照”一致性，而非继续压低编辑器节流频率。当前多标签复用了一个编辑器和一组 ViewModel；局部锁、路径比较和加载序号已经提供保护，但尚未覆盖所有异步边界。

| 编号 | 优先级 | 发现 | 主要影响 |
| --- | --- | --- | --- |
| R01 | P1 | 保存刷新后未重新校验文档；打开/切换不共用操作协调机制 | 另一标签的文本可能写入先前捕获的路径 |
| R02 | P1 | 新建标签未切换独立撤销历史对象 | 旧标签撤销历史被清空、混入其他文档内容 |
| R03 | P1 | 关闭前可能未刷新最新编辑；刷新超时没有失败语义 | 最后一次输入丢失或旧快照被当成保存成功 |
| R04 | P1 | 备份仅覆盖活动标签，未命名标签共用备份键 | 后台/未命名文档崩溃恢复不完整 |
| R05 | P1 | 原子替换失败后回退为覆盖复制 | 失败路径仍可能损坏原文件 |
| R06 | P1 | 文件监控静默丢事件，重载格式写入早于身份检查 | 外部修改漏检、跨标签编码状态污染 |
| R07 | P1 | 非 UTF-8 文本可被有损解码后保存 | 不可逆字符替换 |
| R08 | P2 | 最近访问记录有索引错误、无限插入和初始化悬挂问题 | 功能异常、数据库增长、启动等待 |
| R09 | P2 | 保存/元数据持久化仍有 UI 线程同步工作；历史预算仅按文档 | 卡顿、尾延迟、多标签内存增长 |
| R10 | P2 | 文件树提前枚举折叠目录，后台枚举没有并发上限 | 大目录打开慢、任务与监控句柄膨胀 |
| R11 | P2 | 节流后仍执行全文导出/扫描，长文档仍建立全文 DOM | 大文档输入、打开和切换成本随规模增长 |
| R12 | P2 | RPC 缺少超时/取消，差分协议缺少失步恢复 | Promise/监听器滞留、异常恢复困难 |

P1：优先在下一次面向用户的测试版本前修复和验证；P2：随后分批优化。不是所有 P1 都会在普通单文档使用中触发，但它们影响数据完整性。

## 已有改进应当保留

代码已经有保存串行锁、内容快照哈希、`loadId` 过滤、保存前 `FlushContent`、同目录临时文件写入、目录枚举过期结果过滤、滚动按帧合并、状态去重、编辑器全文任务节流及撤销历史容量限制。不能按旧版本把这些项目重新列为“尚未实现”。

[2026-09-18 审查记录](../CODE_REVIEW.md)是历史记录。例如其“文件仍直接覆盖写入”的描述已被 `SafeFile` 主路径取代；当前问题是**失败回退仍不安全**。已有 [编辑器性能记录](editor-performance.md)含历史测量，但本轮没有复测其中的延迟或内存数值。

## 详细发现

### R01 — 保存与标签切换尚未形成一个一致的操作边界

**依据（代码可确认）**：

- [FileViewModel.cs](../Dev/Typedown.Core/ViewModels/FileViewModel.cs#L404) 的 `Save` 只给保存操作加锁。`SaveCore` 在 429 行捕获 `path`，430 行等待 `FlushContentAsync`，之后从全局 `EditorViewModel.Markdown` 取内容并立即写入；路径复核在写入之后。`SaveAsCore` 在 475 行也存在等待后读取活动文档的问题。
- [TabsViewModel.cs](../Dev/Typedown.Core/ViewModels/TabsViewModel.cs#L160) 的 `SwitchTo` 不使用该锁，`switching` 仅抑制属性同步，没有阻止其他切换进入；其等待后也不再检查目标标签是否还存在。
- [FileViewModel.cs](../Dev/Typedown.Core/ViewModels/FileViewModel.cs#L248) 的 `LoadFile` 在读盘及备份对话框之后直接更新共享状态，没有关联发起时的文档操作身份。

**触发推演**：A 保存进入刷新等待，期间切到 B；等待结束后读取 B 的正文，却仍以 A 的路径写盘。写完才发现路径改变并返回失败，不能撤销已经发生的覆盖。连续打开文件时，较慢的旧读取也可能覆盖较新的活动文档状态。

**建议**：建立不可变保存快照 `{documentId, revision, path, text, format}`。刷新响应必须属于指定文档；刷新后复核身份，再构造快照，写盘只使用快照，不重新读取活动 ViewModel。打开/关闭/切换由同一个文档操作协调器排队或取消旧操作，避免以长时间持有 UI 锁替代身份管理。保存后台文档应提交到对应 `DocumentTab`，不要更新恰好处于活动状态的标签。

**验收**：用可控制完成顺序的假刷新器/文件读取器，覆盖保存 A 等待时切 B、A→B→A、另存为期间切换、连续打开慢 A/快 B、目标标签关闭后等待才结束。比较 A/B 落盘字节、编码、修订号及活动标签，不能只检查返回值。

### R02 — 新建标签共用了旧文档的撤销历史

**依据（代码可确认）**：[TabsViewModel.SnapshotActive](../Dev/Typedown.Core/ViewModels/TabsViewModel.cs#L104) 将 `editor.History` 的引用放进旧标签；`BeginNewTab` 只新建并激活标签，没有把 `EditorViewModel.History` 切换到新标签的历史。随后 [LoadFile 第 298 行](../Dev/Typedown.Core/ViewModels/FileViewModel.cs#L298)或 `NewFileFun` 调用当前 `History.InitHistory`；[InitHistory](../Dev/Typedown.Core/Models/RuntimeModels/ContentHistory.cs#L211)先清空原对象。虽然 [DocumentTab](../Dev/Typedown.Core/Models/RuntimeModels/DocumentTab.cs)自己创建了历史，但该打开路径没有使用它。

**影响**：A 编辑后新开 B，A 保存的历史引用实际被初始化为 B；切回 A 再编辑、撤销，可能丢掉 A 的历史，甚至回到 B 的历史内容。且 `BeginNewTab` 路径未经过 `SwitchTo` 新增的刷新步骤，旧文档节流中的编辑也可能未纳入快照。

**建议**：刷新并冻结旧文档，再为新文档绑定独立历史、正文、光标和加载状态；加载失败回滚整个文档状态。集中实现这一流程，避免“切换已有标签”和“新开标签”各自维护一套不一致的状态迁移。

**验收**：A 连续编辑→新开 B→编辑 B→返回 A→撤销/重做；新建未命名标签也执行同样测试。断言不同文档的历史对象不相同，撤销结果不含其他文档的特征文本，失败加载不清掉原历史。

### R03 — 刷新超时和关闭判断没有可靠的完成条件

**依据（代码可确认）**：

- [FlushContentAsync](../Dev/Typedown.Core/ViewModels/EditorViewModel.cs#L491)等待响应或 500 ms 超时，但不返回哪一种完成，也不检查 `PostMessage` 的发送结果。
- [前端响应](../Dev/Typedown.Editor/src/components/Editor/index.tsx#L130)携带 `text`，宿主 `OnContentFlushed` 只取 token、完成等待，既不校验文档/loadId，也不消费正文。当前正确性依赖先到达的 `MarkdownChange` 和当前加载状态。
- [MainWindow.OnClosing](../Dev/Typedown/Windows/MainWindow.cs#L257)先调用 `AutoSaveFile`，后者先比较宿主哈希才决定是否进入保存/刷新；[AskToSaveAll](../Dev/Typedown.Core/ViewModels/TabsViewModel.cs#L249)也是先依据宿主 `Saved` 判断。前端的大文档报告存在 [250 ms 节流窗口](../Dev/Typedown.Editor/src/components/Muya/lib/index.js#L121)。

**触发推演**：宿主仍认为文件已保存时，用户在尚未报告的时间窗内再次编辑并关闭，关闭路径可能跳过刷新和提示。页面繁忙导致刷新超时时，也会继续写旧内容并可能标记成功。

**建议**：刷新返回带文档身份和修订号的结果，明确区分成功、页面不可用、超时及过期响应。关闭、替换当前预览、新建标签前先取得最新状态再判断是否 dirty。超时可以保留已知快照作恢复材料，但不能把它当作“最新编辑已安全保存”并允许无提示关闭；提供重试或取消关闭。

**验收**：大文件输入后立即关闭窗口/活动标签；丢弃、延迟、乱序或错误 loadId 的刷新响应；页面正在载入时保存。必须保证最新内容被确认写入，或者关闭被阻止并解释原因。

### R04 — 备份设计尚未覆盖多标签文档

**依据（代码可确认）**：[FileViewModel 第 119–167 行](../Dev/Typedown.Core/ViewModels/FileViewModel.cs#L119)的定时保存/备份只访问当前 ViewModel；[AutoBackup.GetBackupFilePath](../Dev/Typedown.Core/Services/AutoBackup.cs#L11)把 null 路径统一转换为空字符串。`AutoBackupFile` 在正文为空或全空白时删除备份，没有把“用户清空了文件”视为可恢复修订。

**影响**：切走的脏标签可能长期不再备份；多个未命名文档映射到同一备份文件。清空正文的有效编辑无法恢复。未变动的脏正文又可能每五秒重复写盘和强制刷新。

**建议**：用稳定的 `documentId` 为备份命名，清单关联路径、编码、修订、更新时间及窗口/标签；轮询所有 dirty 文档，按“最后成功备份修订”跳过重复 I/O。空内容同样可备份，只有明确丢弃或已持久化相应修订后删除。清单和备份更新要有失败恢复策略。

**验收**：两个未命名标签分别编辑；A 未保存时切到 B 并持续编辑；清空已存文件；备份磁盘写满。强制终止后逐一恢复，不能串文档，也不能把失败的备份记为成功。

### R05 — 原子写入的回退破坏了安全保证

**依据（代码可确认）**：[SafeFile.WriteAllBytesAtomicAsync](../Dev/Typedown.Core/Utilities/SafeFile.cs#L35)主路径写临时文件并替换；替换抛出 `IOException`、`UnauthorizedAccessException` 等异常时，执行 `File.Copy(tempPath, path, overwrite: true)`。覆盖复制不是一次原子提交，异常类别也不等于“文件系统不支持原子替换”。`finally` 随后尝试删除临时文件。

**影响**：复制过程中发生中断或错误时，目标可能只剩部分内容；完整临时副本又可能被清理。备份同样调用此工具，因此两个保护层共有这一失败模式。

**建议**：替换失败时优先保持原文件并返回明确错误；对短暂共享冲突作有界重试。确实不支持原子替换的存储应采用单独设计且可恢复的协议，不能静默降级。保留失败时的完整临时内容供恢复，并制定过期清理规则。验证替换后的属性、权限和同步盘行为，不把“Hidden/Temporary”注释等同于所有同步软件都一定忽略。

**测试缺口**：[SafeFileCheck](../Tools/SafeFileCheck/Program.cs)只覆盖成功写和临时文件名过长导致的早期失败，不能证明提交阶段失败仍安全。

**验收**：对临时写、flush、替换、回退及清理分别注入异常/中断；在 Windows 本地盘及实际使用的网络/同步目录验证。结果应是旧文件完整或新文件完整，不能接受截断内容。

### R06 — 外部重载可能漏事件或提前改变其他文档的格式

**依据（代码可确认）**：

- [ScheduleReloadFromDisk](../Dev/Typedown.Core/ViewModels/FileViewModel.cs#L890)在自身写入后一秒直接丢弃事件，没有到期复查。
- [HandleExternalFileChange 第 933–947 行](../Dev/Typedown.Core/ViewModels/FileViewModel.cs#L933)等待读取后先写全局 `FileFormat`，再检查当前路径是否仍匹配。
- `reloadDialogOpened` 仅覆盖对话框阶段，读取阶段可以重入；文件监控未处理 `Error` 后的全量校验。

**建议**：事件只表示“需要复查”，忽略期内置 dirty 标记，到期按磁盘版本/内容重新核对。一次只运行一个重载读取，将 `{text, format, documentId}` 保存在局部快照，身份复核后再一起提交。监控溢出或失效时触发重新扫描；后台标签也记录待复查状态。

**验收**：自身保存后 100 ms 外部再次保存；慢读 A 时切换到不同编码的 B；重载连续触发；监控事件丢失/溢出。B 的格式不能被 A 的读取结果改变，最终外部版本必须被发现。

### R07 — 文本解码不是无损的

**依据（代码可确认）**：[TextFileFormat.ReadAsync/DetectEncoding](../Dev/Typedown.Core/Utilities/TextFileFormat.cs#L33)对没有 BOM 的文件使用默认 UTF-8 解码，没有启用非法字节异常或记录解码损失。当前文件树已经支持普通文本，GBK 等内容可能出现替代字符，再保存即覆盖原字节。

**建议**：先严格校验 UTF-8；无法无损解码时阻止自动覆盖，提供明确编码选择或只读查看。不要依赖猜测编码后无提示自动保存。保留原始字节或恢复副本，记录转换动作。

**验收**：UTF-8、UTF-8 BOM、UTF-16、非法 UTF-8、GBK、中英文混合、混合换行、空文件。未确认转换时不得将有损解码结果写回；编码转换后重新打开应与预期一致。

### R08 — 最近访问记录有可直接修复的错误与增长问题

**依据（代码可确认）**：

1. [AccessHistory 第 133 行](../Dev/Typedown.Core/Services/AccessHistory.cs#L133)裁剪文件夹列表时用了 `FileRecentlyOpened.Count - 1`。没有文件历史、打开第 11 个不同文件夹时会尝试 `RemoveAt(-1)`；两种列表长度不同时也可能删除错误位置。
2. [RecordFileHistory](../Dev/Typedown.Core/Services/AccessHistory.cs#L25)每次新增数据库行，保存成功也会调用它；模型以递增 Id 为主键，没有按路径去重。`Take(10)` 发生在内存去重之前，同一文件的多条近期记录会挤掉其他文件。
3. [初始化](../Dev/Typedown.Core/Services/AccessHistory.cs#L145)仅在成功时完成 `initializedTask`；数据库读取/迁移失败后，`EnsureInitialized` 的等待不会收到失败结果。

**建议**：立即修正列表索引；将“最近访问”设计为规范化路径唯一记录并更新时间，或为事件历史单独保留有期限的表。不必每次自动保存都更新“最近打开”。初始化要传递错误并允许重试，异步后台任务必须有异常观测。

**验收**：零文件历史下打开 11 个目录；同一文件保存上千次后重启，最近列表仍有不同文件且表大小有界；数据库不可读/锁定时启动不无限等待。

### R09 — UI 线程同步工作与多标签内存预算

**依据（代码路径可确认；卡顿程度待测）**：

- [SafeFile 第 47–48 行](../Dev/Typedown.Core/Utilities/SafeFile.cs#L47)异步写后同步 `Flush(true)`，后续还有同步替换/复制；没有显式把整个持久化阶段移出调用方 UI 上下文。
- [TextFileFormat](../Dev/Typedown.Core/Utilities/TextFileFormat.cs#L33)读盘后全量解码、扫描换行；保存前构造完整文本/字节副本。
- [CursorMemory.Flush](../Dev/Typedown.Core/Services/CursorMemory.cs#L93)由 UI 定时器调用，同步序列化并写文件，写前就清 dirty，失败不会主动重试；[SessionMemory.Save](../Dev/Typedown.Core/Services/SessionMemory.cs#L46)也直接同步覆盖 JSON。
- [ContentHistory](../Dev/Typedown.Core/Models/RuntimeModels/ContentHistory.cs#L17)的 32M 字符上限是每个历史对象，不是窗口总预算，也不包含 pending 与其他正文副本。隔离历史对象后，多个大标签会放大总内存占用。

**建议**：文档状态快照在 UI 线程捕获，编码、刷新和持久化进入受控后台队列；只将完成结果送回 UI。元数据使用可恢复写入，成功后按修订清 dirty。设置窗口级历史总预算、单文档额度和回收规则，同时保留最基本撤销能力。

**验收**：慢盘/网络盘保存期间持续输入和滚动，记录 UI 主线程长任务、输入 p95/p99、保存尾延迟。10/50 个大标签切换及关闭后，记录托管堆和 WebView 堆是否回落。上限数值应按测量确定。

### R10 — 目录树需要限制提前加载与并发

**依据（代码可确认）**：[ExplorerItem.OnIsExpandedChanged](../Dev/Typedown.Core/Models/RuntimeModels/ExplorerItem.cs#L108)把子节点全部设为 `IsWatching`；[CreateChild](../Dev/Typedown.Core/Models/RuntimeModels/ExplorerItem.cs#L247)也继承父节点展开状态。于是展开一个有大量子文件夹的目录，会对尚未展开的子目录同时执行 `Task.Run` 枚举并建立监控。过期版本检查阻止旧结果提交，但没有停止已经运行的 I/O。

**建议**：以“展开时加载”为默认，只为折叠节点保留是否有子项的轻量信息；必要的预取使用有界队列、取消令牌和预算。监控采用共享或按需策略，UI 集合分批提交，避免每个后台任务完成都触发大量布局。

**验收**：宽目录（例如 1000 个子目录）、深目录、10 万文件、网络目录；快速展开/折叠/切换。统计首次可交互时间、最大并发枚举数、监控句柄数和取消后残余 I/O，而非仅测最终加载完成时间。

### R11 — 继续优化全文工作前，先固定行为和性能基线

**依据（代码可确认）**：[Muya.dispatchChangeContentChange](../Dev/Typedown.Editor/src/components/Muya/lib/index.js#L150)仍获得全文 Markdown/光标、字数、大纲；[transport.ts](../Dev/Typedown.Editor/src/services/transport.ts#L42)先全文 JSON 序列化再扫描前后缀做差分；宿主解析完整状态并计算正文哈希。差分减少传输字节，不等于这些 CPU/分配开销也变为增量。当前 `content-visibility` 只减少部分布局，全文模型和 DOM 仍存在。

**建议**：

1. 测清全文导出、统计、大纲、JSON、宿主哈希/历史各阶段 CPU 和分配，再优先缓存未变化结果、按块修订增量维护统计与大纲。
2. 文档同步和 UI 辅助状态分开频率，但“保存/关闭/切换”必须受 R01–R03 的一致性协议约束，不能仅靠加大延迟优化。
3. 只有打开/切换仍达不到产品目标时，再评估分块渲染或视口虚拟化；在实验分支验证 IME、查找、表格、光标、大纲、打印/导出，避免立即更换内核或复活已知有状态泄漏的多实例缓存。
4. [webpack 配置](../Dev/Typedown.Editor/config-overrides.js)强制单 chunk；图表/导出等功能的懒加载可作为后续实验，但必须证明 WebView2 本地资源加载和离线使用仍可靠。

**验收**：固定机器、浏览器、语料和电源模式；覆盖 1万/10万/30万/100万字符及高密度表格/代码/公式；报告打开、切换、按键 p50/p95/p99、长任务、消息数/字节、托管堆/JS 堆。单独测浏览器桩和真实 Windows 宿主，不能把仓库历史基准数值当成本次结果。

### R12 — 消息协议需要错误收敛与生命周期管理

**依据（代码可确认）**：[remoteFunction](../Dev/Typedown.Editor/src/services/transport.ts#L28)仅在收到响应时删除监听器，没有超时、取消或发送失败清理；宿主 [Transport.EmitWebViewMessage](../Dev/Typedown.Core/Services/Transport.cs#L28)的 JSON 解析与差分处理在 invoke 分支异常捕获之外，直接使用字典、Substring 范围和 `JToken.Parse`。差分没有基线版本或请求全量重发的路径。

**影响**：页面重载/宿主未回包时 Promise 和监听器滞留；异常载荷或基线失步不能局部恢复，`async void` 异常可能进入应用级异常处理。这里描述容错缺口，不据此认定存在可利用安全漏洞。

**建议**：请求统一 deadline/取消并在所有结束路径释放监听器；页面会话结束批量终止请求。消息包含会话、文档身份与序号，检查基线和范围；失步时请求完整快照，不尝试应用未知差分。记录可定位的错误，但日志中不要写入整篇文档或凭据。

**验收**：发送抛错、无回复、重复/过期回复、页面重载、首次收到 diff、越界差分、损坏 JSON。请求在时限内结束、监听器数量回归基线，编辑器可重新同步而不污染其他文档。

## 验证结果与测试缺口

本轮实际执行：

```text
cd Dev/Typedown.Editor
CI=true npm test -- --watchAll=false --runInBand
# 2 个套件、8 个测试通过；有 Browserslist 数据过旧提示。
```

这些测试覆盖 transport/scrollbar，不能证明宿主保存、标签、编码、崩溃恢复正确。本环境 PATH 中无 `dotnet`，没有执行 C# 检查器、Windows 构建或桌面 UI 测试；本轮也没有重建前端或运行性能基准。

当前 CI 已有翻译检查、前端测试/构建以及 Windows x64/ARM64 打包。检查 [.github/workflows/build.yml](../.github/workflows/build.yml)后，未发现运行 `Tools/SafeFileCheck`、`Tools/WatcherSaveCheck`、`Tools/EditorBench` 或 C# 测试的步骤。

建议的测试层次：

| 层次 | 首批必须覆盖 | 放置方式 |
| --- | --- | --- |
| 宿主状态单元测试 | R01–R04：文档身份、历史隔离、刷新超时、关闭、备份 | 抽取不依赖 XAML 的文档协调器；假文件系统、假时钟、可手动完成的刷新任务 |
| 文件系统集成测试 | 替换阶段故障、编码、权限、并发读写、监控溢出 | 保留已有 SafeFileCheck，并增加 Windows 真实 API 路径；按阶段注入失败 |
| 前端行为测试 | handshake、tab-state、tab-stress、roundtrip、spec | 使用新构建且记录提交号的 bundle；固定依赖和浏览器版本 |
| Windows 端到端 | 输入后立即保存/关闭、快速切换、网络目录、崩溃恢复 | 同时断言屏幕状态与实际文件字节，覆盖 x64；ARM64 至少冒烟 |
| 性能回归 | 长文档、宽目录、多标签、慢盘 | 专用或稳定机器，多轮比较；阈值基于基线波动，避免共享 CI 的噪声阻断发布 |

`WatcherSaveCheck` 的重命名决策是在检查器中重写的逻辑，不是直接调用宿主处理器；建议抽出共享的纯决策函数，避免测试复制实现后与生产代码分叉。`EditorBench` 的桩宿主也不能代替真实的 C# 标签/保存验证。

## 分阶段实施建议

1. **数据一致性阶段**：先写能失败的 R01–R03 测试；引入文档身份/修订和操作协调器，修复历史对象所有权、关闭与刷新语义。接着修复 R04–R07 并做 Windows 文件系统验证。退出条件：交错操作不串文档、故障不损坏原文件、脏文档均可恢复。
2. **低风险性能与容错阶段**：修复 R08 的明确索引和初始化问题，给最近访问和备份去重；隔离 UI 线程持久化，限定目录枚举并发，完善 RPC 清理。每批提交带对应行为测试与前后指标。
3. **大文档阶段**：在稳定基线上测 R09–R11，先减少重复全文工作和总内存，再决定是否投入增量协议/虚拟化。不要同时更换编辑器内核、宿主框架与保存模型，否则无法定位性能收益和数据回归。

建议追踪的硬指标：错误文档写入次数必须为 0；持久化失败不得标为成功；恢复测试不得遗漏脏文档。性能指标采用实测基线：输入/打开/切换/保存的分位延迟、UI 长任务、每分钟写盘量、数据库大小、目录任务/句柄峰值以及多标签总内存。
