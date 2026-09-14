# 1330 · 当前开放缺口清单

> **生成时间**：2026-09-14（初版）　**口径**：代码级审计（逐条查证到文件:行号），非推测。
> **最近更新**：2026-09-14 —— A1–A5 与 C1/C2/C4/C5/C11/C12 已实现并**闭合**，实现位置见末尾 [§G 已闭合清单](#g-已闭合清单2026-09-14)；仍开放 C3/C6/C7/C8/C9/C10、B 组、D 组。
> **本文取代** [voicetodo-master.md](../voicetodo-master.md) 第 4.2 / 4.3 / 4.4 节中已过期的部分——那三节写于 2026-09-11 重构中途，其 P0/P1/P2 大半已修复，**不可再作为待办依据**。
> 平台能力导致的「不修项」见 [06-execution-plan.md](06-execution-plan.md) §7.1；已修复批次见 [07-batch-completion-report.md](07-batch-completion-report.md)。

---

## 0. 审计前提

- 双 TFM 构建 **0 错误**（Windows TFM 实测 **43 警告**（2026-09-14 复测）；Android 42 警告本轮未复核。均为 CS0618 过时、CA1416/CA1422 API 级别、CS860x 可空性等非阻断类）。
- 回归自检 `tools/VoiceTodo.SelfCheck` **23/23 通过**。
- 故 master 文档 4.2 的 P0-1/2/5/6/7/8/9、4.3 的 P1-1/2/6/9/10 **均已闭合**，本文不再列入。

---

## A. 会静默失效的（优先级最高：功能在，但用户看不到失效）

| # | 缺口 | 证据 | 后果 |
|---|---|---|---|
| A1 | **通知权限从未申请** | `Platforms/Android/AndroidManifest.xml:7` 只**声明** `POST_NOTIFICATIONS`；全仓无 `RequestAsync<Permissions.PostNotifications>`。麦克风有申请（`Pages/VoiceCapturePage.xaml.cs:248`），通知没有。`Pages/SettingsPage.xaml.cs:187` 只 `CheckStatusAsync` | **Android 13+ 新装用户收不到任何提醒**，且 UI 不提示。整批「提醒闭环」工作在真机上等于没生效 |
| A2 | **运行中训练被静默忽略** | `Pages/TimerPage.xaml.cs:812` 运行中直接 `return`；`Services/TimerLauncher.cs:25-30` 只置位 `PendingStart/PendingAutoStart`，无冲突判断 | 用户在已有训练时又说一句训练口令，**零反馈**，以为口令没被识别 |
| A3 | **日历页待办行不可点** | `Pages/CalendarPage.xaml.cs:193-205` 仅 `SelectDay`，行无手势绑定 | 从日历看到任务，却点不进编辑（master P0-4 的残留项） |
| A4 | **Nag 无法按任务停止** | 只有设置页全局开关 `Pages/SettingsPage.xaml.cs:238-242`；无「这个任务别再催了」入口 | 单条任务持续催促时，用户只能全局关掉催促 |
| A5 ✅ | **录音 WAV 保留且无清理策略** | `PlatformServices/AndroidAudioMicrophoneCapture.cs:35-37` 写入 `AppDataDirectory/captures/capture-<时间戳>.wav`；`:54` 注释明确「超时/外部取消 → 自然结束并**保留文件**」，仅 `CancelAsync`（`:73-77`）丢弃 | 每说一句就留一个 WAV，长期累积占存储；且「说了什么」以音频形式留在磁盘，用户不知情、无清理入口 |

## B. 常驻语音会话：状态机已具备，外围三件全无

`Services/VoiceSessionController.cs` 是完整的会话层状态机（会话层未开启/已开启、在飞录音句柄、结束会话），但：

| # | 缺口 | 证据 |
|---|---|---|
| B1 | 无**麦克风类型**前台服务 | `PlatformServices/TimerForegroundService.cs:13` 注释自述「麦克风 FGS 属 DEV-06 未做」；`:43` 注册的只有计时用 `specialUse` |
| B2 | 无**会话常驻通知** | 现有常驻通知属计时（`TimerForegroundService.cs:226-227`），会话无通知与停止入口 |
| B3 | 无**全局会话状态条** | `Services/VoiceSessionController.cs:43` 的 `Changed` 事件**无任何 UI 订阅** |

> 结论：会话能「活着」，但用户看不见、锁屏会被系统回收、后台录音会被平台掐断。这是产品主方向（常驻语音交互）当前最大的空洞。

## C. 完全缺失的功能

| # | 缺口 | 证据 / 说明 |
|---|---|---|
| C1 | 删除撤销 | 全仓无 Snackbar / Toast / Undo；`Pages/TaskEditorPage.xaml.cs:130` 明确提示「删除后不可恢复」。另：`ViewModels/TodoRowVm.cs:28,40` 的 `DeleteCommand` 未在任何 XAML 绑定，疑似死代码 |
| C2 | 导出 / 导入 / 备份 | `Export`、`Import`、`Share`、`FilePicker`、`ShareFile` 全仓零命中 |
| C3 | 历史留痕「再次开始」 | `TimerSession` 行仅支持改名（`Pages/CalendarPage.xaml.cs:395-410`）；「最近使用」来自 `TimerItem` 计划快照而非留痕（`Pages/TimerPage.xaml.cs:211-231`） |
| C4 ✅ | 自定义模板保存 / 复用 | 仅硬编码 tabata / hiit / custom chip（`Pages/TimerEditorPage.xaml.cs:194-204`），无保存与读取通路 |
| C5 ✅ | 清单页新建入口 | `Pages/ListsPage.xaml` 只有搜索 + 筛选 chips + 列表，无新建入口 |
| C6 | 日历「当日添加」 | `Pages/CalendarPage.xaml.cs:193-205` 无预填日期的添加入口 |
| C7 | 静默时段 / 免打扰 | `QuietHours` / 静默时段 / 免打扰 全仓零命中 |
| C8 | 多计时并行 | 单实例：`ViewModels/MainViewModel.cs:125,156`，`RunningTimerHub.Current` 为单快照（master P1-3） |
| C9 | **无障碍** | `SemanticProperties` **0 处**；无字号自适应（`DynamicResource` 字号 / `AppThemeBinding` / `OnPlatform` 字号均 0 处）；仅 1 处 `MinimumHeightRequest="44"`（`Pages/TaskEditorPage.xaml:34`）；48dp 只在样式注释里提（`Resources/Styles.xaml:8,214`）→ **200% 字号与读屏不达标** |
| C10 | **i18n 未收口** | `.xaml`：`AppShell.xaml:19/23/27`（今天/清单/计时）、`Pages/CalendarPage.xaml:6`、`Pages/SettingsPage.xaml:106`；`.xaml.cs` 约 30+ 处，例：`SettingsPage.xaml.cs:154,159`、`TaskEditorPage.xaml.cs:130`、`CalendarPage.xaml.cs:284` |
| C11 | **TTS 嗓音是死设置** | `Preferences["ttsVoice"]` 只被 `Pages/SettingsPage.xaml.cs:70,229` 读写，`PlatformServices/NativeTextToSpeech.cs` 无任何选音逻辑 → 用户改了不生效（master P1-7 仍成立） |
| C12 | 日志 / 诊断 | 大量 `catch { }` 静默降级（通知、TTS、权限、模型提取、音频焦点），出故障无法定位（master P2-10） |

> ✅ = 2026-09-14 已实现并闭合（实现位置见 §G）。**C 组 12 项已闭合 6 项**，仍开放 C3 / C6 / C7 / C8 / C9 / C10。

## D. 工程与仓库卫生

| # | 项 | 说明 |
|---|---|---|
| D1 | 非 git 仓库 | `D:\FromGit\todolist` 无 `.git` → 任何改动都**没有回退快照** |
| D2 | 非随包模型不可用 | `Resources/Models/manifest.json` 中 `PackInBuild=false` 的条目（英文 tiny / base）在设置里可选，但无运行时下载器，提取不到文件会静默失败 |
| D3 | 存储是 JSON 非 SQLite | 每次操作全量读写、读无并发保护、无索引、无迁移（master P2-1，属架构取舍，非缺陷） |
| D4 | 平台覆盖 | 仅 Android + Windows（无 iOS / MacCatalyst） |
| D5 | 构建耗时 | Android Release 全量 AOT 约 13.5 分钟（99 程序集 × 4 ABI）→ 日常调试建议加 `-p:AndroidEnableProfiledAot=false` |

## E. 已排除的「假缺口」（勿重复误报）

| 项 | 结论 |
|---|---|
| `Pages/ListsPage.xaml.cs:177` 抛 `NotImplementedException` | **不是缺陷**。位于单向 `IValueConverter.ConvertBack`，是 XAML 惯用写法 |
| `Services/MockDataSeeder.cs` 会污染真实库 | **已合规**。仅在 `#if DEBUG`（`Pages/MainPage.xaml.cs:54-55`）且真实库不存在时写入（`MockDataSeeder.cs:53`） |
| `PlatformServices/DemoMicrophoneCapture.cs` 仍是默认录音 | **已不是**。DI 按平台注册真实实现（`MauiProgram.cs:56-63`），桩仅留作测试 |
| `System.Drawing.Common` 4.7.0 高危漏洞（NU1904） | **已不复现**。该告警来自已删除的历史 `build.log`；当前 `obj/project.assets.json` 中已无此包，最近三次构建均无该告警 |

---

## F. 推进状态与下一步

| 选项 | 内容 | 状态 |
|---|---|---|
| A | 静默失效「可信 4+1」：A1 通知权限 + A2 训练冲突确认 + A3 日历行可点 + A4 Nag 单任务停止 + A5 录音清理 | ✅ **已完成**（2026-09-14） |
| C-1 | C1 删除撤销 + C2 导出/导入 + C4 自定义模板 + C5 清单页新建 + C11 TTS 嗓音 + C12 日志地基 | ✅ **已完成**（2026-09-14） |
| C-2 | C3 历史留痕「再次开始」+ C6 日历当日添加 + C7 静默时段 | 待做，改动集中在 CalendarPage / TimerPage / SettingsPage |
| C-3 | C8 多计时并行 | 待做；与 A2 新引入的「替换 / 取消」语义互斥，需先定产品决策 |
| C-4 | C9 无障碍 + C10 i18n 收口 | 待做；需改全部 XAML，冲突面最大，建议单独一轮 |
| B | 打通常驻会话外围（B1 麦克风 FGS + B2 常驻通知 + B3 全局状态条） | 待做；产品主方向，工作量最大、需真机调 |
| D | 还工程债（D1 git init + D2 模型说明 + 文档收口） | 待做；不动功能，风险最低 |

**原建议顺序 A → C → B 中的 A 已全部完成、C 完成一半。建议下一步：D（`git init` 先给改动上回退快照）→ C-2 → B。**

---

## G. 已闭合清单（2026-09-14）

### A 组 · 静默失效（5/5 闭合）

| # | 实现位置 | 做法 |
|---|---|---|
| A1 | 新建 `Services/NotificationPermission.cs`；接入 `Pages/OnboardingPage.xaml.cs`、`Pages/TextAddPage.xaml.cs`、`Pages/VoiceCapturePage.xaml.cs`、`Pages/TaskEditorPage.xaml.cs` | `EnsureAsync()` 运行时申请 `POST_NOTIFICATIONS`（Android 13+ 守卫，异常/拒绝返回 false）。触发点＝引导完成 + 保存带提醒待办前；**拒绝不阻断保存**，改提示 `Perm_RemindOffNoNotif`（「已保存，但通知权限未开启，提醒不会响起」） |
| A2 | `Pages/TimerPage.xaml.cs`（订阅 `TimerLauncher.Changed`） | 运行中收到新训练请求 → 弹「替换 / 取消」（`Conflict_*` 文案），消除原静默 `return` |
| A3 | `Pages/CalendarPage.xaml` | 待办行加 `TapGestureRecognizer` → `TaskEditorPage?id=`；原 `SelectDay` 点日行为保留 |
| A4 | Core `ITodoChangeDispatcher` 增 `StopNagAsync(int)` + `Services/TodoChangeDispatcher.cs` 实现；`Pages/ReminderPage.xaml(.cs)` 加「停止催促」按钮 | 取消实体全部关联通知（复用既有 Cancel 路径，不新造 ID 规则）并关闭 `Reminder.NagMode`，避免重排再度唤起 |
| A5 | 新建 `Services/RecordingJanitor.cs`；`App.xaml.cs` 启动时 fire-and-forget 触发；同步 `docs/privacy.md` | `CleanupOld()` 清理 `captures/` 下 **7 天前** `.wav`（按拍板策略：保留 7 天后自动清理） |

### C 组 · 完全缺失（6/12 闭合）

| # | 实现位置 | 做法 |
|---|---|---|
| C1 | `Pages/ListsPage.xaml(.cs)` | `SwipeView` 左滑删除 + 底部撤销条（2.5s 自动隐藏；`CancellationTokenSource` + 代次守卫防旧定时器误隐藏，新删除覆盖旧快照）。撤销经 `ITodoChangeDispatcher.CreateAsync(snapshot)` 重建（新 Id） |
| C2 | 新建 `Services/TodoBackupService.cs`；`Pages/SettingsPage.xaml(.cs)` 加「数据」分组 | 导出 todos/timers/sessions → JSON（带 schema 版本 + 导出时间）→ `Share`；导入经 `FilePicker` 逐条 `AddTodoAsync/AddTimerAsync`（新 Id，**刻意不触发提醒调度**）；取消选择静默返回 |
| C4 | 新建 `Services/TemplateStore.cs`；`Pages/TimerEditorPage.xaml(.cs)` | `Preferences` 存模板 JSON 数组（同名覆盖、上限 8 拒绝）；模板行动态追加用户 chip（复用固定 chip 选中态配色）+「＋ 存为模板」+ 长按删除带确认 |
| C5 | `Pages/ListsPage.xaml(.cs)` | 标题行右侧「＋」→ `TextAddPage`（审计确认 `TaskEditorPage` 无参**并非**新建模式，会弹「找不到该任务」回退） |
| C11 | `PlatformServices/NativeTextToSpeech.cs` 新增 `ResolveLang` | 读 `Preferences["ttsVoice"]`（trim + 长度上限 35，坏值不致命）；优先级 偏好 > culture > zh。Android `Locale.ForLanguageTag`；Windows 在 `AllVoices` 按 `Voice.Language` 前缀匹配，匹配不到静默降级 |
| C12 | 新建 `Services/AppLog.cs`；接入 `SettingsPage` 两处原空 `catch` 与 C1/C2/C4 异常分支 | 落点 `AppDataDirectory/logs/app-yyyyMMdd.log`，`Info/Warn/Error`，`CleanupOld(7)`、`ExportAsync`；**内部全 try/catch，日志自身绝不抛** |

### 本轮新增 AppResources 键（28）

- **A 组（8）**：`Perm_RemindOffNoNotif`、`Conflict_Title`、`Conflict_Text`、`Conflict_Replace`、`Conflict_Cancel`、`Conflict_Kept`、`NagStop_Button`、`NagStop_Done`
- **C 组（20）**：`Undo_DeletedFormat`、`Undo_Action`、`Undo_Failed`、`DataGroup`、`ExportData`、`ImportData`、`ExportDoneFormat`、`ImportDoneFormat`、`ImportFailed`、`ExportFailed`、`ExportLogs`、`LogsNone`、`SaveAsTemplate`、`SaveAsTemplateTitle`、`TplNamePrompt`、`TplSavedFormat`、`TplDeleteConfirm`、`TplDeleteTitle`、`TplLimitReached`、`AddTodo`

（三处同步：`AppResources.cs` / `AppResources.resx` / `AppResources.zh.resx`；均为追加，未改动已有键）

### 验证

- Windows TFM 构建：**0 错误 / 43 警告**（2026-09-14 实测；43 条全为既有 CS0618/CS8629/CS8601/CS8602/CS8670，**无一条来自本轮新增文件**）。
- 按约定未跑 Android 构建（Release 全量 AOT 约 13.5 分钟，本轮跳过）。

### 本轮遗留

- **C12 只做了地基**：`App.xaml.cs` 全局异常处理未接入 `AppLog`；其余散落的 `catch {}`（通知、TTS、模型提取、音频焦点等）未逐一替换，属后续清扫项。
- **C4 长按删除**：.NET MAUI 无 `LongPressGestureRecognizer`，改用 `Button.Pressed` 起算 650ms + `Released` 取消的等价实现（附守卫避免与 tap 冲突）。
- **C2 导入**直接落库、不经 dispatcher（批量导入不适合逐条排提醒）——与「页面变更走 dispatcher」契约不冲突，但需留意导入项**不会**自动获得提醒。
- **A1 为「保存时申请」而非「启动时申请」**：用户拒绝后不阻断保存，仅在带提醒的待办保存时提示；后续如需更早引导可再加设置页一键开启入口。
