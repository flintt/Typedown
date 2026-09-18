# 代码审查与性能优化（2026-09-18）

本轮检查文件保存/恢复/外部重载，以及 WebView2 消息与滚动更新路径；并非全仓库安全审计。未进行 Windows 桌面端性能实测。

## 已修复

| 优先级 | 问题与触发条件 | 改动 |
| --- | --- | --- |
| 高 | 保存异步写盘期间继续输入，完成后以最新编辑哈希标记已保存，可能让未落盘内容在关闭时被忽略 | 保存捕获内容快照，完成后按快照更新磁盘哈希；有新编辑时继续保持未保存状态并保留备份；保存返回值不允许关闭流程误认为全部内容已落盘 |
| 高 | 手动保存、另存为与自动保存可以同时写盘，完成顺序和状态更新可能交错 | 同一窗口内用 SemaphoreSlim 串行保存；检查等待期间的文件切换与窗口释放；自动保存定时任务防止重入 |
| 高 | 外部重载异步读取或弹窗期间切换文件，旧内容可能应用到新路径 | 捕获读取路径并在异步边界后检查；拒绝重载时重新计算未保存状态 |
| 中 | 新建文件发送字符串，但前端 LoadFile 处理器解构的是对象 | 统一发送 text/basePath；恢复未命名文档时，以恢复内容初始化撤销历史 |
| 中 | 打开文件的备份校验使用上一份文档 CurrentHash | 改用刚读入文件的 FileHash |
| 中 | 滚动、resize、ResizeObserver 同一帧重复读取布局并跨 WebView2 发消息 | requestAnimationFrame 合并，执行时读取最新状态；滚动监听使用 passive |
| 中 | 不变的滚动和格式状态仍产生差分消息与宿主反序列化 | 仅对 OnScroll、SelectionFormats 去重；保留重复命令和 FileLoaded 生命周期事件 |
| 中 | 发送差分失败后发送端缓存已前进，后续差分无法由宿主正确还原 | 成功发送后才更新差分基线 |

## 验证

- `CI=true npm test -- --watchAll=false --runInBand`：2 个测试套件、8 个用例通过。
- 回归覆盖：中文/emoji 差分还原、插入删除、发送失败后的基线、重复命令、重复状态、滚动事件合并、后续帧继续更新。
- 测试中同帧 300 次滚动/尺寸通知合并为一次发送；101 次相同状态发送合并为一次。这是调用次数验证，不代表真实桌面端耗时或帧率提升比例。
- 修改的 4 个 TypeScript 文件通过 ESLint；`git diff --check` 通过。
- `CI=false GENERATE_SOURCEMAP=false npm run build`：生产构建通过。构建仍提示 Browserslist 数据过旧和包体积偏大（主 JS gzip 约 1.12 MB）；本次未做包体积优化。
- CI 前端构建前新增上述回归测试。
- 独立 `tsc --noEmit` 被已安装依赖 `@types/d3-dispatch/index.d.ts:91` 的 const 类型参数语法阻挡，当前 TypeScript 版本无法解析。此次未修改依赖版本。
- 当前 Linux 环境没有 .NET SDK，C# 未编译或执行。需要在 Windows 上验证：保存中输入、连续保存/另存为、备份恢复、新建文件、外部修改后选择保留、重载读取期间切换文件。

## 仍需处理或进一步验证

1. **高：文件写入不是原子替换。** `FileViewModel.WriteAllText` 和 `AutoBackup.Backup` 仍直接覆盖目标；写入中断风险仍存在。本次保存串行化不能解决断电保护。后续需要临时文件、可靠刷新和替换策略，并验证 Windows 文件权限、网络盘与文件监控交互。
2. **中：外部文件监控存在盲区。** `ScheduleReloadFromDisk` 在自身写入后一秒直接丢弃事件，可能漏掉同期真正的外部编辑；同一路径关闭后重新打开也不能仅凭路径识别文档会话。需要引入会话标识与延迟复查策略。
3. **中：自动备份重复 I/O。** 未保存内容不变时仍可能每五秒重写备份；可按文档修订去重，但需要同步处理备份删除、恢复与失败重试。
4. **中：PowerShell 图床服务未实现。** `Dev/Typedown/Services/PowerShellService.cs` 仍抛出 NotImplementedException。
5. **待评估：WebView2 信任边界。** `Config.WebView2Args` 启用了 disable-web-security 和 allow-file-access-from-files，需要结合 HTML 清洗、本地资源访问和宿主消息来源检查单独审查，不能仅凭配置认定可利用漏洞。
