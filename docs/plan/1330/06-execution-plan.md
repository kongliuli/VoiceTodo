# 1330 · 实施计划（对照代码现状）

> 生成时间：2026-09-11。本文是 [03-delivery-plan.md](03-delivery-plan.md) 的落地细化：把 DEV 条目映射到当前代码的具体差距与文件落点，并给出执行顺序与并发拆分。
> 优先级与需求冲突一律以 [05-spoken-training-model.md](05-spoken-training-model.md) 为准；证据细节见 [page-optimization-review.md](../page-optimization-review.md)。
> 审查清单（5 高 + 4 中 + 2 语音兼容缺口）的补完结果、构建/自检证据与额外修复项见 [07-batch-completion-report.md](07-batch-completion-report.md)。

---

## 1. 现状快照（2026-09-11 实测 · 2026-09-14 复核）

> **口径**：本节只记录「已落地」的事实，判定以代码落点为准。
> **开放缺口不在本节** —— 见 [08-open-gaps.md](08-open-gaps.md)（当前唯一有效的缺口清单）与下方 §7.2。

| 项 | 状态 |
|---|---|
| 双 TFM 构建 | Windows / Android 均 **0 错误**（2026-09-11 复审实测：Windows 34 警告 / Android 42 警告，均为 CS0618 过时、CA1416 API 级别、CS860x 可空性等非阻断类；命令：`dotnet build -f net10.0-android\|net10.0-windows10.0.19041.0 src/VoiceTodo.Maui/VoiceTodo.Maui.csproj`） |
| 页面骨架 | 14 屏对应 ContentPage 均已建立（含 TaskEditor/VoiceCapture/TextAdd/TimerEditor/Reminder/Onboarding/Calendar） |
| 间歇解析 | `RuleBasedIntentParser` 产出统一 `TrainingPlan`（动作名/组数/运动/休息/末组休息/倒数窗/阶段播报），含中文数词量词、否定修正、计次与无单位歧义追问、示例/疑问句不触发；`Phases` 由 `Expand()` 派生 |
| 留痕 | `TimerSession` + 仓库 Session 三方法 + 日历「计时记录」图层与改名已完成 |
| 运行引擎 | 准备态 + 暂停冻结 + 跨页 RunningTimerHub；跑完写 Session（completed/cancelled/interrupted） |
| 语音链 | 已注册真实录音（Android `AndroidAudioMicrophoneCapture` / Windows `WindowsAudioMicrophoneCapture`）；`DemoMicrophoneCapture` 保留但**不再默认注入**（原记录"仍注册 Demo 桩"已过时） |
| 提醒 | **DEV-02 闭环已完成**：`TodoChangeDispatcher` 为唯一变更入口，语音/文字/编辑/清单/首页/日历（`TodoRowVm`）统一走此入口；完成/取消勾选/删除/延后同步取消或重排通知；通知 ID 由实体派生并持久化 |
| 重复任务 | 主提醒槽位按 `IsRecurring`/`RecurrenceRule` 循环（`NotificationRepeat.Daily/Weekly`）；「每工作日」因插件能力限制近似为 Daily（见 §7 局限） |
| Windows 排期 | `INotificationScheduler.SupportsFutureScheduling` = false；上层如实反馈"未安排"，不再谎报；到点由 MainPage 应用内检查兜底 |
| 中文口语时间 | `ChineseTimeParser` 支持中文数词与「半」（五分钟后／半小时／十五分钟／三点），普通提醒与倒计时路径与训练计划口径一致 |
| 存储 | `JsonFileTodoRepository` 读取失败抛 `StorageException` 并保留 `*.corrupt-*` 副本、写入临时文件原子替换；页面加载/保存/留痕入口已补异常处理与重试提示 |
| 常驻会话 | `VoiceSessionController` 承载会话层（未开启/已开启）与在飞录音句柄，离开录音页不取消本句、重新进入可接管；「结束会话」为独立生命周期终点 |
| 设置持久化 | `AppSettings` 全量落地 Preferences（场景/延后/预提醒/铃声/动效/音频共存/Nag/语言），`App` 启动回读语言与设置 |
| 回归自检 | `tools/VoiceTodo.SelfCheck`（纯 .NET、离线、无外部包）：训练计划/中文时间/解析边界（含提醒词优先级）/通知 ID/Nag/快照恢复/损坏读与原子写/调度决策 共 **23 项用例，全部通过** |

### 05 模型带来的推翻项（对已完成工作的修订）

| 已实现（按旧 D3） | 05 新要求 | 动作 |
|---|---|---|
| VoiceCapturePage「确认后再开始」 | 完整有效指令**自动创建并开始**，05 页是运行反馈不是确认页 | 已完成：完整计划/计时经 `TimerLauncher.RequestStart(..., autoStart:true)` 直通运行态；缺槽进追问 |
| 解析 Title=「间歇训练」/主题词 | 动作名=用户文本（「深蹲」） | 已完成：解析器提取动作名 |
| 计划=Phases 扁平段 | 统一计划模型：动作/组数/运动/休息/**末组休息**/**倒数窗口**/阶段指示 | 已完成：`TrainingPlan`（§2） |
| 无 | 「10 次」≠「10 组」、否定修正、最终文本去重、示例不触发 | 已完成：解析与草稿层边界齐备（自检覆盖） |
| 无 | 已有训练运行不静默覆盖 | 待办：见 §7 遗留项（并发训练保护） |

---

## 2. 统一训练计划模型（DEV-04 契约，先行冻结）

新文件 `src/VoiceTodo.Core/Models/TrainingPlan.cs`：

```csharp
/// <summary>统一训练计划：语音/文字/模板/历史四入口共用的语义表示。</summary>
public class TrainingPlan
{
    public string ActionName { get; set; } = "";     // 动作名（用户文本，如「深蹲」）
    public int Rounds { get; set; }                  // 组数（≥1）
    public TimeSpan WorkDuration { get; set; }       // 每组运动时长
    public TimeSpan RestDuration { get; set; }       // 组间休息时长（可为 0）
    public bool RestAfterLastRound { get; set; }     // 末组休息，默认 false（深蹲例：140s=02:20）
    public int CountdownWindowSeconds { get; set; }  // 阶段末倒数：5 / 3 / 0=关
    public bool PhaseAnnouncements { get; set; } = true; // 阶段指示开关

    public TimeSpan TotalDuration =>
        Rounds * WorkDuration + (Rounds - 1 + (RestAfterLastRound ? 1 : 0)) * RestDuration;
}
```

- `TrainingPlan.Expand()` 生成执行段序列（复用现有 SchedItem 展平逻辑，移入 Core 供三入口共用）。
- `TimerItem` 增加 `TrainingPlan? Plan` 快照；`TimerSession` 记录快照引用（已有 Phases 保留兼容）。
- `VoiceCommand` 增加 `TrainingPlan? Plan` 与 `List<string> MissingSlots`（追问依据）。
- 解析产物优先填 `Plan`；`Phases` 由 `Expand()` 派生，不再作为解析器直接输出（消灭三处表示不一致的根因）。

---

## 3. 阶段推进（对应 03 §2，含代码落点）

### 阶段 0 · 基线（DEV-00）— 已达成，留证即可

- [x] 双 TFM 构建 0 错误（本文档头部命令）。
- [ ] 记录工具链版本（SDK 10.0.401 / workload android 36.1.69 / maui-windows 10.0.20）到构建记录。
- [ ] 技术小验证并行做：Android FGS 类型与 targetSdk=36 合并 Manifest 核对（review §6 平台结论）。

### 阶段 1 · 存储与提醒可信（DEV-01 / DEV-02）+ 真实录音并行（DEV-03）

**DEV-01 存储安全** — `Core/Services/TodoRepository.cs`
- `LoadAsync` 失败不再返回空 Store：抛 `StorageReadException`（含损坏文件副本路径 `*.corrupt-yyyyMMddHHmmss`）。
- `SaveAsync` 改临时文件 + `File.Move(overwrite:true)` 原子替换。
- 查询接口加 `Task<List<T>> GetXxxSafeAsync()` 或页面层捕获区分「空库 / 读取失败」。
- UI：MainPage/ListsPage/CalendarPage 读取失败显示重试条，禁止把空库写回。
- **MockDataSeeder 隔离**：样例改写独立文件 `voicetodo.sample.json`，仅 DEBUG 且真实库不存在时启用； MainViewModel/UI 不再感知种子。

**DEV-02 提醒闭环** — 新 `Maui/Services/TodoChangeDispatcher.cs`
- 唯一入口：`CreateAsync / RescheduleAsync / CompleteAsync / DeleteAsync / PostponeAsync`（内部 = repo 变更 + 调度器同步）。
- 通知关联 ID：`TodoItem.NotificationIds`（主提醒/预提醒/Nag）持久化，调度器用实体 ID 派生，重启不漂移。
- Nag：每次调度带终止条件（N 节奏 + 上限时间），完成/删除即 `CancelAsync` 全部关联 ID。
- PreAlert：主提醒前 X 分钟一条（读取 `ReminderSettings.PreAlert`）。
- Snooze 语义修正：调度时不再整体后移；延后 = 取消旧 + 排新（仅该次）。
- 三入口接线：VoiceCapturePage.OnConfirmAdd / TextAddPage.OnConfirmAdd / TaskEditorPage.OnSaveClicked 改走 Dispatcher；保存反馈区分「已保存+提醒已安排 / 未开启」。
- Windows `WindowsNotificationScheduler.CancelAsync` 空实现补齐（记录 Tag 并移除）。

**DEV-03 真实录音与权限** — `Maui/PlatformServices/`
- 新 `RealMicrophoneCapture.cs`（Android：`AudioRecord` 16kHz mono PCM→WAV；Windows：WinRT `MediaCapture`→WAV），替换 DI 注册，Demo 仅留测试。
- 权限：`Permissions.RequestAsync<Permissions.Microphone>`（Android）/ Windows 麦克风设置引导；允许/拒绝/永久拒绝三分支 UI（VoiceCapturePage 状态机：等待授权/录音中/整理中/未听清/麦克风不可用）。
- 「完成本句」保留音频识别，「取消本句」丢弃（独立信号，不共用取消令牌）。

**并发拆分（文件边界不重叠）**：
| 代理 | 文件集 |
|---|---|
| A | Core：TodoRepository.cs、新 StorageException、Models 微调；MockDataSeeder.cs |
| B | Maui：新 TodoChangeDispatcher、三入口接线、通知调度器（Android+Windows）、ReminderPage、RESX |
| C | Maui/PlatformServices：RealMicrophoneCapture（双平台）、MauiProgram 注册、VoiceCapturePage 权限与状态机、RESX |

> B 与 C 都可能碰 VoiceCapturePage —— 冲突点收敛：C 只改录音/权限/状态段，B 只改 OnConfirmAdd 保存段；集成时主线合并。

### 阶段 2 · 训练主链路（DEV-04 → DEV-05）

**DEV-04 统一计划与口述直通**（依赖 §2 模型落地）
- 解析器扩展（`RuleBasedIntentParser.cs`）：动作名提取（「深蹲10组」→ 深蹲）；「10 次」→ 不支持计次、询问按时长；单位歧义（无单位数字）进 `MissingSlots`；同义词 组/轮、每组/每轮、持续/做、歇；否定修正「不是10组是8组」更新草稿字段；示例/疑问句不触发（意图分层：Create vs Ask）。
- **去重**：识别最终结果 hash，同结果不重复创建（VoiceCapturePage 层）。
- VoiceCapturePage 改造：完整 Plan → `Dispatcher`…不，训练不走 Dispatcher → `repo.AddTimerAsync(计划快照)` + `TimerLauncher.RequestStart`（自动开始，跳过 Confirm 态）→ 导航 TimerPage 运行视图（=05 反馈：已开始 + 暂停/停止纠错入口）；缺槽 → 追问态（保留已识别字段）。
- 已有训练运行时：RequestStart 前查 `RunningTimerHub.Current`，弹「替换/取消」明确选择。
- TimerEditorPage 08 改通用计划编辑器（动作/组数/运动/休息/末组休息/倒数窗），承接模板编辑与「停止后纠错」；最近使用（`OnRecentStart`）按 `item.Plan` 重建执行段，不再退化成普通倒计时。
- 模板/历史复用：历史记录（TimerSession）提供「再次开始」复制完整快照。

**DEV-05 活动计时持久化与后台**
- RunningTimerHub 快照每秒落盘（`running-timer.json`：计划、EndAt、SchedIdx、StartedAt、PausedByUser）；启动恢复：EndAt 未到→继续；跨段挂起→按真实经过时间推进，无法对应→标记「上次训练中断」，不记 completed。
- EndRun 异常路径区分 `interrupted` Outcome（TimerSession 扩展枚举值）。
- Android：计时前台服务（FGS specialUse/shortService 按验证结论选型）+ 常驻通知（含暂停/停止动作）；普通计时不占用麦克风。
- 末秒倒数播报（§4 音频队列）也在本项落地。

### 阶段 3 · 常驻会话（DEV-06）

- 会话状态机（D1：手动开→常驻→手动关；无空闲超时）：`Services/VoiceSessionController`；两层状态（会话层：未开启/已开启/系统中断；输入层：等待/采集/处理/待确认/失败）。
- 完成本句 / 取消本句 / 结束会话三控制分离；结束会话不停止训练，结束训练不关会话。
- 全局状态条（01/02 顶部）+ Android FGS microphone 类型 + 常驻通知停止入口。
- 依赖 DEV-03（真录音）与 DEV-05（后台经验）完成。

### 阶段 4 · 页面与设置收尾（DEV-07 / DEV-08 / DEV-10）

- DEV-07：清单就地添加、日历行编辑/当日添加（预填日期）、草稿保护（手工修改不被重新解析覆盖）、删除撤销。
- DEV-08：AppSettings 全量持久化（Scene/Snooze/PreAlert/铃声/动效/倒数窗口/末组休息默认）+ 启动回读语言 + 设置页 12 分层改版（权限可操作、高级页、隐私入口）。
- DEV-10：48dp 命中区核对（CheckCircle 26×26 等）、320/360/390dp、200% 字号、读屏。

### 阶段 5 · 扩展（DEV-09）：多计时、导出导入、静默时段——核心验收通过后再排。

---

## 4. 末秒倒数与播报队列（05 §报数，单独设计）

新 `Maui/Services/AnnouncerQueue.cs`：
- 单一音频队列：阶段提示（「第1组，深蹲」「休息10秒」）、倒数（5/3 窗逐秒）、完成（「训练完成」）。
- 规则：入队带绝对截止时刻，出队时已过期即丢弃；暂停清空队列；恢复不补播；播报不阻塞计时推进（fire-and-forget + 队列串行）。
- 短阶段（时长 < 倒数窗）只报实际剩余秒数；短口令优先（用提示音 + 数字，不读长句）。
- NextUp 卡片在末组显示「——」（已有行为保留，验收点）。

---

## 5. 验收清单整合（03 §5 九条 + 05 专项）

执行时按下表逐条留证（构建/单测/设备三层分开记录）：

| # | 验收 | 对应 |
|---|---|---|
| 1 | 「深蹲10组，每组持续5秒钟，休息10秒」→ 自动开始 10 组，总长 02:20（末休开→02:30） | DEV-04 |
| 2 | 语音/模板/历史三入口同一计划：阶段、组数、总时长一致；最近使用保留间歇结构 | DEV-04 |
| 3 | 第 3 组运动剩 3 秒时全程剩 01:48；5s/3s 倒数各只播一次；播报不延长计时 | DEV-04/05 |
| 4 | 切页/锁屏/切应用/进程回收/重启分别验证：轮次与冻结剩余正确；中断≠完成 | DEV-05 |
| 5 | 保存任务确实排提醒；完成/删除/延后真正取消或替换（关 App 验证系统通知） | DEV-02 |
| 6 | 权限拒绝有文字替代/恢复入口；保存失败保留输入 | DEV-03/07 |
| 7 | 损坏 JSON/写失败不被空库覆盖；设置重启保持 | DEV-01/08 |
| 8 | 关闭会话训练继续；结束训练会话保持 | DEV-06 |
| 9 | 「10 次」追问；缺槽追问不猜；重复最终文本只建一次；未知单位先问 | DEV-04 |
| 10 | 末组不指示下一组；暂停清待播、恢复不补播 | DEV-04/05 |
| 11 | 320/360dp、200% 字号、读屏可完成核心操作 | DEV-10 |

---

## 6. 执行排程与并发拆分总览

```
波次1（并行3代理）：DEV-01存储 | DEV-02提醒 | DEV-03录音     ← 文件边界互斥
波次2（串行先行）：  TrainingPlan 模型落地（§2 契约，主线自做，半天粒度）
波次2（并行2代理）：DEV-04解析+口述直通 | DEV-05持久化+后台+倒数
波次3：            DEV-06 常驻会话（依赖波次1/2）
波次4（并行2代理）：DEV-07页面闭环 | DEV-08设置持久化；DEV-10 贯穿
每波次后主线集成：双 TFM 构建 + 冒烟（构建失败不出波）
```

- 单元检查最小集：TrainingPlan 总时长与 Expand、解析器边界句、倒数队列过期丢弃、仓库损坏读/原子写、通知 ID 派生。模拟时间/录音/通知/文件失败，不启动真实 GUI。
- 设备验收缺口如实标注：锁屏、FGS、系统通知需真机，本轮以 Windows + 模拟器覆盖能覆盖的部分。

---

## 7. 局限与遗留项

> 本节 **7.1 局限**为平台 / 插件能力所致，**不作为缺陷修复**，现状已在 UI 如实反馈；
> **7.2 遗留项**为已确认存在但尚未排期的开放问题。
> 完整开放缺口清单以 [08-open-gaps.md](08-open-gaps.md) 为准。

### 7.1 平台与插件能力局限（不修，如实反馈）

| # | 局限 | 原因 | 现状 |
|---|---|---|---|
| L1 | 「每工作日」重复近似为 `Daily` | `Plugin.LocalNotification` 只有 Daily / Weekly 粒度，无 weekday 掩码 | 主提醒槽位按 Daily 近似排期；UI 不宣称精确的工作日语义 |
| L2 | Windows 端无未来定时通知 | WindowsAppSDK `AppNotifications` 不具备「定时在未来触发」能力 | `WindowsNotificationScheduler.SupportsFutureScheduling = false`，上层**如实提示「未安排」而不谎报**；到点由应用内检查兜底 |
| L3 | Android 7.x 无「压低背景音」 | `AudioFocusRequestClass` 需 API 26+，而工程 `SupportedOSPlatformVersion` 为 24.0 | 焦点请求构造包在 `try/catch` 内（`PlatformServices/AudioCoexist.cs:25-40`）→ API 24/25 上**静默降级为不做 ducking，不崩溃**；CA1416 告警保留。若需彻底消除，可加 `#if ANDROID26_0_OR_GREATER` 分支回退旧 `RequestAudioFocus` 重载 |
| L4 | 安装包仅含 `arm64-v8a` + `x86_64` | .NET Android 默认 RID 集合 | 32 位 `armeabi-v7a` 老机无法安装 |
| L5 | 非随包模型不可用 | 无运行时模型下载器 | `Resources/Models/manifest.json` 中 `PackInBuild=false` 的条目在设置里可选但提取不到文件，静默失败（见 §7.2 R4） |
| L6 | 锁屏 / 后台 / 系统通知无自动化验收 | 需真机 | 本轮以构建 + 自检 + Windows / 模拟器覆盖能覆盖的部分；真机项已在上方验收清单标注 |

### 7.2 遗留项（已确认存在，尚未排期）

| # | 遗留项 | 证据 | 影响 |
|---|---|---|---|
| R1 | **已有训练运行时不静默覆盖的保护缺失** | `Pages/TimerPage.xaml.cs:812` 运行中直接 `return`；`Services/TimerLauncher.cs:25-30` 只置位 | 用户在已有训练时再说训练口令，零反馈 |
| R2 | 通知权限（`POST_NOTIFICATIONS`）无运行时申请 | Manifest 仅声明；`Pages/SettingsPage.xaml.cs:187` 只 `CheckStatusAsync` | Android 13+ 新装设备收不到提醒 |
| R3 | 日历页待办行不可点 | `Pages/CalendarPage.xaml.cs:193-205` 仅 `SelectDay` | 日历入口无法进编辑 |
| R4 | 非随包模型静默失败 | 见 §7.1 L5 | 设置里选了却不生效 |
| R5 | TTS 嗓音是死设置 | `Preferences["ttsVoice"]` 只写不读 | 用户改了不生效 |
