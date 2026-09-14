# 07 · 审查清单批次补完报告

> 完成时间：2026-09-11。本文记录 [page-optimization-review.md](../page-optimization-review.md) 所列「未完善内容」清单（5 高 + 4 中 + 2 语音兼容缺口）的补完结果、构建/自检证据，以及本轮额外发现并修复的阻断项。
> 上游依据：[06-execution-plan.md](06-execution-plan.md)、[05-spoken-training-model.md](05-spoken-training-model.md)。

---

## 1. 交付状态（实测）

| 项 | 结果 | 命令 |
|---|---|---|
| Windows 目标构建 | **0 错误** / 34 警告 | `dotnet build -f net10.0-windows10.0.19041.0 src/VoiceTodo.Maui/VoiceTodo.Maui.csproj` |
| Android 目标构建 | **0 错误** / 42 警告 | `dotnet build -f net10.0-android src/VoiceTodo.Maui/VoiceTodo.Maui.csproj` |
| 回归自检 | **23/23 通过**（`SELF-CHECK OK`） | `dotnet run --project tools/VoiceTodo.SelfCheck/VoiceTodo.SelfCheck.csproj` |

警告性质（均非阻断，无需处理）：`CS0618` `DisplayAlert/DisplayActionSheet` 过时（迁移到 `*Async` 属独立小项）、`CA1416/CA1422` Android API 级别提示（代码已有 `SdkInt` 运行期守卫，分析器看不到）、少量 `CS860x` 可空性提示。

---

## 2. 清单逐项对照

### 高优先级

| # | 问题 | 落点与做法 |
|---|---|---|
| 高① | 完成/删除未全面取消提醒（`TodoRowVm` 绕过调度入口） | `TodoRowVm` 注入 `ITodoChangeDispatcher`；勾选走 `SetDoneAsync`（完成→取消、取消勾选→重排）并失败回滚；删除走 `DeleteAsync`。清单/日历/首页三处 `TodoRowVm` 构造统一传入同一入口 |
| 高② | Windows 提醒虚假报告「已安排」 | `INotificationScheduler` 新增 `SupportsFutureScheduling`；`WindowsNotificationScheduler` 置 `false` 且 `ScheduleTodoAsync` 不登记槽位；`TodoChangeDispatcher` 对不支持平台**不置** `ReminderScheduled`，并回 `ReminderMessage` 说明原因 |
| 高③ | 每日/每周重复任务不真正循环 | `LocalNotificationScheduler` 主提醒槽位带 `RecurrenceOf(item)` → `NotificationRepeat.Daily/Weekly` |
| 高④ | 完整口述计划未自动开始训练 | `TimerLauncher.RequestStart(item, autoStart:true)` 跨页传递；`TimerPage` 消费 `PendingAutoStart` 直接 `StartPreparedRun()`，不再停在准备态 |
| 高⑤ | 存储异常页面处理未补齐 | 新增 `UserAlerts`（统一提示 + 确认重试）；页面加载/保存/留痕的 `async void` 入口全部补捕获 `StorageException` |

### 中优先级

| # | 问题 | 落点与做法 |
|---|---|---|
| 中① | 训练复用/纠错丢计划信息 | 「最近使用」按 `item.Plan` 重建执行段（不再退化成普通倒计时）；纠错写入 `PendingEditPlan` 后由 `TimerEditorPage.OnAppearing` 读取预填 |
| 中② | 设置重启保持不完整 | `AppSettings` 全量走 `Preferences`；新增 `Load()` 于 `App` 构造回读语言与全部设置；设置页开关直接写 `AppSettings.*` |
| 中③ | 文字草稿缺手工修改保护 | 草稿字段加 `_titleEdited/_dateEdited/_timeEdited/_repeatEdited` 标记；重新解析只填未手工编辑的字段 |
| 中④ | 常驻语音会话未完成 | 新增 `VoiceSessionController`（会话层与页面解耦）；离开录音页不取消在飞录音，重进可接管；「结束会话」为独立生命周期终点 |

### 语音兼容缺口

| # | 问题 | 落点与做法 |
|---|---|---|
| ① | Whisper 英文模型文件名未对齐 manifest | `WhisperSpeechRecognizer` 改为 `GetActive(ModelKind.Asr)` + `ResolveModelFile`（优先 `Options["ModelFile"]` → `Files` 中 `ggml-*.bin` → 目录内任意 `ggml-*.bin`），不再硬编码文件名 |
| ② | 普通中文时间解析覆盖不足 | `ChineseTimeParser` 支持中文数词与「半」：五分钟后／半小时／十五分钟／半小时后／三点 |

---

## 3. 本轮额外发现并修复的阻断项

清单之外，构建与自检暴露了 4 个必须修的缺陷：

| # | 症状 | 根因 | 修复 |
|---|---|---|---|
| A | 14 处 `CS0117`：`UserAlerts` 不含 `ShowStorageErrorAsync` 定义 | 各页面调用的快捷提示方法只写了调用方、未落实现 | `UserAlerts` 补 `ShowStorageErrorAsync(Exception)` 与 `(string)` 两个重载（均按「存储异常 + 原因」展示） |
| B | Android `CS1729`：`PropertyAttribute` 无 2 参构造 | `Android.App.PropertyAttribute` 签名是 `(string name)`，`Value` 为独立可写属性 | 改为 `[Property("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE", Value = "...")]` |
| C | Android 5 处 `CS0019`：`BuildVersionCodes` 与 `int` 无法比较 | Android 36 绑定中 `Build.VERSION.SdkInt` 已是枚举 | 改用枚举成员 `BuildVersionCodes.O`(26) / `Q`(29) / `S`(31) |
| D | 「五分钟后提醒我关火」被当成倒计时 | `RuleBasedIntentParser` 见「分钟」即判 `Timer`；`VoiceCapturePage.IsTimerDraft` 再因 `Duration>0` 成立 → 提醒语被做成倒计时草稿 | 新增提醒信号词（提醒我/提醒/记得/通知我/催我/催促）并**优先于时长词**；`Type=Todo` + `TriggerAt` 才是正确归处 |

> D 的影响最实质：只要提醒句里带时长词（「10 分钟后提醒我开会」等），提醒就永远不会落成待办、也不会排通知——直接打穿高①/高②所在的提醒闭环。

---

## 4. 自检工程

`tools/VoiceTodo.SelfCheck`（纯 .NET、离线、无外部包），23 项用例覆盖：训练计划总时长与 `Expand`、中文口语时间、解析器边界（计次/否定修正/疑问句/提醒词优先级）、通知 ID 派生、Nag 策略、运行快照恢复（原位/跨段/连跨两段/暂停/流尽）、仓库损坏读与原子写、调度决策（排期/不谎报/完成取消/取消勾选重排/过去时间）。

本轮修正了自检自身的 2 处错误期望（**非产品缺陷**）：

1. 用例名写「→ Timer + 时长」，断言却要求 `Todo`——自相矛盾。拆成两例：提醒句断言 `Todo`（并新增「五分钟倒计时」断言 `Timer`）。
2. 跨段落点算错：在段长 `30/10/30/10`、`EndAt=now+30` 的快照上取 `now+45`，已越过 `work(0~30)` 与 `rest(30~40)`，落点应为下标 2 而非 1；改为 `now+35` 验落 `rest`（Case=1）、并新增 `now+45` 验连跨两段（Case=2）。

---

## 5. 待真机验收（代码已就绪，本轮不在自动化范围）

1. 锁屏 / 切应用 / 进程被杀重启：轮次与冻结剩余正确，中断 ≠ 完成。
2. Android FGS 保活与常驻通知（暂停/停止动作）在 targetSdk=36（specialUse）下的实际行为。
3. 系统通知：保存任务确实排提醒；完成/删除/延后真正取消或替换（关 App 验证）。
4. 小屏（320/360dp）与 200% 字号、读屏。

## 6. 已知局限

1. 「每工作日」因 `Plugin.LocalNotification` 能力限制近似为 `Daily`（见 06 §7）。
2. Windows 端无未来定时能力（`SupportsFutureScheduling=false`），由 MainPage 应用内检查兜底；提醒依赖应用存活。
3. 提醒词优先于时长词是**规则**判定，非语义模型：如需更细粒度（如「每 5 分钟提醒我」这类周期提醒）需在 `RecurrenceRule` 层另作扩展。
