# VoiceTodo 现状盘点与缺漏清单

> **[已合并]** 本文内容已并入 `voicetodo-master.md`（第四部分）。保留本文件仅作历史备查，后续请以总纲为准。
>
> 盘点时间：2026-09-11 10:32–11:00
> 方法：通读 `src/` 全部源码 + `docs/`（research / plan / design / models）+ 脚本与工程文件，并对两个目标做**实际构建验证**。

---

## 0. 重要前提：分析期间项目正被并发修改

本次盘点过程中，仓库源码被**另一个进程/会话持续改写**，时间戳如下：

| 时间 | 文件 |
|---|---|
| 10:37:00 | `Services/RunningTimerHub.cs`（**新增**） |
| 10:46:56 → 10:47:29 | `Pages/SettingsPage.xaml(.cs)` |
| 10:48:40 → 10:49:38 | `Pages/CalendarPage.xaml(.cs)`、`Pages/ListsPage.xaml(.cs)`（**新增**） |
| 10:51:52 → 10:53:25 | `Pages/TimerPage.xaml(.cs)` |
| 10:53:46 | `Drawables/RingDrawable.cs` |
| 10:54:37 → 10:55:08 | `Pages/MainPage.xaml(.cs)` |
| 10:55:41 → 10:55:47 | `ViewModels/MainViewModel.cs`、`ViewModels/TodoRowVm.cs` |

这是一次**按 `docs/design/maui/` 设计母版 V1 落地的重构**（新增「清单」页、文字添加、计时运行/暂停、首页今日视图、设置分组）。
因此：**本清单反映的是 10:56 快照**；其中"正在实现"的条目可能在你读到本文时已变化。`Core` 层（`VoicePipeline` / 解析器 / 仓库 / 通知调度）在同一时段**未被修改**。

---

## 1. 项目是什么

| 项 | 内容 |
|---|---|
| 产品 | 隐私优先、端侧离线的语音待办 + 临时定时器 App（健身/家务/开车免提） |
| 形态 | .NET 10 + .NET MAUI，目标 `net10.0-android` + `net10.0-windows10.0.19041.0` |
| 架构 | `VoiceTodo.Core`（纯 .NET 领域层，无 MAUI 依赖）+ `VoiceTodo.Maui`（UI / 平台服务） |
| ASR | whisper.net（whisper.cpp），模型随包分发 + 首次提取到 AppData |
| TTS | 平台原生（Android `TextToSpeech` / Windows `SpeechSynthesizer`） |
| NLU | 规则/关键词 + 自写中英时间解析器（离线、零模型） |
| 存储 | JSON 文件（`%LocalAppData%/VoiceTodo/voicetodo.json`） |
| 通知 | Android 走 `Plugin.LocalNotification 13.0.0`；Windows 走 WinAppSDK toast |

---

## 2. 当前构建状态（实测，⚠️ 当前 HEAD 编译不通过）

环境：dotnet SDK `10.0.401`；workload android `36.1.69`、maui-windows `10.0.20`、ios / maccatalyst 均已安装。

| 目标 | 结果 |
|---|---|
| `VoiceTodo.Core` (net10.0) | ✅ 通过（1 警告 CS8670：`ModelEntry.Options` 初始化项可能隐式引用 null 成员） |
| `VoiceTodo.Maui` (net10.0-windows) | ❌ **失败：3 个错误** |
| `VoiceTodo.Maui` (net10.0-android) | ❌ **失败：3 个错误** |

### 2.1 阻塞构建的 3 个错误（均在重构中新引入）

| # | 位置 | 错误 | 原因与修法 |
|---|---|---|---|
| E1 | `Pages/MainPage.xaml.cs:37-38` | CS1061：`CollectionView` 未包含 `CollectionChanged` | `CollectionView` 不是可观察集合，没有该事件。应改为订阅 ViewModel 暴露的 `ObservableCollection`（`Todos` / `DoneTodos`），或 `(TodoList.ItemsSource as INotifyCollectionChanged)?.CollectionChanged` |
| E2 | `Pages/TimerPage.xaml.cs:276` | CS8852：只能对 `init` 属性赋值 | `RunningTimerInfo.Title` 是 `required … { get; init; }`，而 `BeginPhase()` 里在构造后 `_info.Title = _runTitle;`。改为 `{ get; set; }`，或每个阶段 `Publish` 一个新实例 |
| E3 | 同上文件 | （同因）`RunningTimerInfo` 是共享可变实例，`Title` 只在构造时赋值 | 见 E2 |

### 2.2 非阻塞但值得修的告警

| 类别 | 位置 | 说明 |
|---|---|---|
| CS0618 | `TimerPage.xaml.cs:400` | `DisplayAlert` 已过时 → 用 `DisplayAlertAsync` |
| CS8600/8601/8602/8604/8618 | `PlatformServices/AudioCoexist.cs` 多处 | 空值分析告警：`_audioManager` 未用 `required`，`AudioAttributes` 链式调用可能为 null |
| **CA1416** | `AudioCoexist.cs:31/46/52` | **`AudioFocusRequestClass` 需要 Android API 26+，但工程 `SupportedOSPlatformVersion` 为 24.0** → 在 Android 7.x 上会运行时抛异常。需加 `#if ANDROID26_0_OR_GREATER` 分支走旧 `RequestAudioFocus(listener, streamType, hint)`，或把最低版本提到 26 |

> 补充：10:36 之前（重构开始前）两个目标都是**可以编译通过**的。`docs/plan/build-status.md` 里"Windows 被 WMC0001 卡死、所有目标被 API 不匹配卡死"的结论**已完全失效**，该文件应更新或归档。

---

## 3. 功能盘点：对照 14 屏设计母版

`docs/design/maui/screens-v1/` 定义了 14 张目标页面。以 10:56 快照为准：

| # | 设计屏 | 状态 | 说明 |
|---|---|---|---|
| 01 | 今天（首页） | 🟢 已实现 | 摘要行、运行计时卡（500ms 心跳）、今日安排、已完成折叠、待安排入口、查看全部 |
| 02 | 清单（待安排/今天/未来/已完成 + 搜索） | 🟢 已实现 | `ListsPage`，四段筛选 chip + 即时搜索 + 空态文案 |
| 03 | 任务编辑 | 🔴 未实现 | 无页面；`ITodoRepository` 也无 Update API |
| 04 | 语音聆听 | 🟡 部分 | 有聆听态与按钮态切换，但无"处理中"、无取消 |
| 05 | 识别确认 | 🔴 未实现 | 识别结果**直接落库**，无草稿确认/纠正 |
| 06 | 文字添加 | 🟡 部分 | 用 `DisplayPromptAsync` 弹窗实现（非独立页），复用同一解析管线 |
| 07 | 计时列表（多计时器） | 🟡 部分 | 有倒计时/间歇分段、常用计时（5/10/25 分）、最近使用；仍为**单个**运行计时（`RunningTimerHub` 注释明示 MVP 只承载一个） |
| 08 | 间歇配置 | 🟡 部分 | 仅 3 个预设，无法自定义 work/rest/轮次 |
| 09 | 计时运行 | 🟢 基本实现 | 大数字、环形进度、轮次、接下来、语音提示；结束有确认弹窗 |
| 10 | 计时暂停 | 🟢 已实现 | 暂停冻结剩余、继续按冻结值重算；跨页面继续运行 |
| 11 | 到期提醒（应用内响应） | 🔴 未实现 | 无完成/延后闭环，只有系统通知 |
| 12 | 设置 | 🟢 基本实现 | 四分组、权限状态展示、语言即时生效并持久化、自动保存提示 |
| 13 | 首次使用/权限引导 | 🔴 未实现 | 无 onboarding |
| 14 | 日历与当日安排 | 🟡 部分 | 月/周视图 + 当日待办已有；仍不显示定时器 |

统计：**已实现 5、部分实现 5、未实现 4**。

---

## 4. 缺漏清单

### P0 — 阻断核心价值 / 当前不可用

| # | 缺漏 | 证据 | 影响 |
|---|---|---|---|
| P0-1 | **当前不能编译**（E1/E2 两个文件 3 个错误） | 见第 2 节 | 无法产出安装包 |
| P0-2 | **没有真实录音**：麦克风是桩，永远返回静音 WAV | `PlatformServices/DemoMicrophoneCapture.cs`；`MauiProgram.cs:55` 无条件注册 Demo；`IMicrophoneCapture` 无任何平台实现 | 语音输入形同虚设，识别恒为空 |
| P0-3 | **无权限申请流程**：只"查看"不"请求" | `SettingsPage.LoadPermissionStatusAsync` 仅调 `Permissions.CheckStatusAsync`，全仓无 `RequestAsync` | Android 13+ 录音/通知被拒且无法引导授权 |
| P0-4 | **无法编辑已有待办/定时器** | `ITodoRepository` 无 Update；无编辑页（设计 03） | 时间/标题/提醒全不可改，只能删了重建 |
| P0-5 | **识别结果不可确认/纠正** | `VoicePipeline.ProcessTextAsync` 直接 `AddAsync`（设计 05 缺失） | 识别错的待办进库后无法修正 |
| P0-6 | **提醒取消失效（id 错位）** | 调度器自增 `_nextId`（进程内）vs 仓库持久化 `Id`；`CancelAsync(match.Id)` 传的是仓库 id；重启后必然错位 | 删除/完成待办后提醒仍会响 |
| P0-7 | **NagMode 无法停止** | `LocalNotificationScheduler.ScheduleAsync`：1 分钟周期重复，无终止条件 | 开启后持续打扰，且取消不掉 |
| P0-8 | **重复任务不生效** | `RecurrenceRule` 只存不排，无任何消费方 | "每天/每周一"不会重复提醒 |
| P0-9 | **预提醒未实现；Snooze 语义用错** | `PreAlert` 无消费方；`ScheduleAsync` 把 Snooze 当作"首触发时间整体后移" | 配了 Snooze 会让**所有**提醒晚点响 |
| P0-10 | **Windows 端提醒残缺** | `WindowsNotificationScheduler`：无按钮、无 Nag/Snooze、`CancelAsync` 空实现 | 桌面端提醒不可靠 |

### P1 — 核心功能不完整

| # | 缺漏 | 说明 |
|---|---|---|
| P1-1 | 语音无法生成可变间歇序列 | `VoiceCommand.Phases` 始终为 `null`，解析器从不填充 |
| P1-2 | 无自定义间歇编辑器 | 无法输入 work/rest/轮次（设计 08） |
| P1-3 | 无多计时器并行 | `RunningTimerHub` 明示"MVP 只承载单个活动计时"（设计 07） |
| P1-4 | 无应用内到期提醒响应 | 无完成/延后按钮闭环（设计 11） |
| P1-5 | 无首次使用/权限引导 | 设计 13 |
| P1-6 | 计时不落库、不排通知 | `TimerPage` 运行态只在内存 + `RunningTimerHub`；退出应用即丢，与其他页面/通知无联动 |
| P1-7 | **TTS 嗓音设置是死设置** | `Preferences["ttsVoice"]` 写入后**无人读取**；`docs/models.md` 承诺的 TTS 模型下拉框也不存在 |
| P1-8 | 非随包模型不可用 | 无运行时下载；设置里能选但提取不到文件，静默失败 |
| P1-9 | **部分设置不持久化** | `Scene` / `Snooze` / `PreAlert` / `CustomSoundPath` / `UseAnimations` 仍是内存态，重启即丢（仅 `lang` / `nagMode` / `audioCoexist` / `ttsVoice` 落了 Preferences） |
| P1-10 | 启动不应用已保存语言 | `App.xaml.cs` 用 `CultureInfo.CurrentUICulture`，不读 `Preferences["lang"]`；只有进设置页才生效 |
| P1-11 | i18n 覆盖不全 | 硬编码中文：设置页"预提醒""背景音乐共存"、"（未随包，需切换打包）"、`MainPage.FormatDuration` 的单位、语音按钮"听…"等；切语言不刷新已渲染页面 |
| P1-12 | 识别质量/健壮性 | 固定 8 秒；写死 16kHz 与 44 字节偏移；无 VAD/流式；忽略 manifest 的 `ModelFile`；整段 PCM 全读入内存（`WhisperSpeechRecognizer`） |
| P1-13 | 不是"免提" | 每次必须点按钮，无连续监听/唤醒词/硬件键，与开车/健身免提定位矛盾 |
| P1-14 | 空识别结果静默失败 | `MainViewModel.ListenAsync` 对空文本无任何反馈 |

### P2 — 工程化 / 体验 / 文档

| # | 缺漏 | 说明 |
|---|---|---|
| P2-1 | 存储是 JSON 而非 SQLite | 与计划不符；每次操作全量读写、读操作无并发保护、无索引、无迁移 |
| P2-2 | **无任何测试** | 计划宣称 Core"可单测"；sln 仅 2 个项目，零测试工程、零 CI |
| P2-3 | 无 README / 无版本号 / 无关于页 / 无隐私说明 | 隐私是核心卖点却无对外声明 |
| P2-4 | 平台覆盖缩水 | 计划称四平台，csproj 只有 Android + Windows |
| P2-5 | **文档与代码不同步** | `build-status.md` 结论失效；`models.md` 列的 SenseVoice/Paraformer/Kokoro 与 manifest 实际 3 条 whisper 不符；计划里的 sherpa-onnx TTS 已换为原生 TTS；`models.md` 承诺的双下拉框只有 ASR |
| P2-6 | 无解析结果可视化 | 研究文档要求"语音原文 + 解析结果可视化"；只显示原文，不显示识别出的类型/动作/时间 |
| P2-7 | 无删除撤销 / 无导出导入备份 | — |
| P2-8 | `AsyncCommand` 异常不可观测 | `Execute` 为 `async void`；`CanExecuteChanged` 从不触发，`CanExecute` 形同虚设 |
| P2-9 | 无障碍未落实 | 设计规范要求 48dp 触控目标、字体缩放；导航图标用纯文字，未见验证 |
| P2-10 | 无日志/诊断 | 大量 `catch { }` 静默降级（通知、TTS、权限、模型提取），故障不可定位 |
| P2-11 | 残留临时产物 | `src/VoiceTodo.Maui/build.log`（调试遗留） |
| P2-12 | `SampleRate`/帧率类细节 | `AudioCoexist` 用 `Media` usage 抢焦点播 TTS，语义上可商榷；`NativeTextToSpeech` Windows 侧用 `text.Length/12` 估算播报时长，长文本会被截断 |

---

## 5. 建议的下一步（按投入产出排序）

1. **先修 3 个编译错误（E1/E2）**，让 HEAD 回到可构建——这是后续一切验证的前提。
2. **补真实录音 + 权限申请**（P0-2/P0-3）：实现 `AndroidMicrophoneCapture`（AudioRecord 16k mono → WAV）与 Windows WASAPI 采集；在首页/首次使用时调 `Permissions.RequestAsync`。
3. **补识别确认页（设计 05）+ 任务编辑页（设计 03）**：仓库加 `UpdateAsync`，识别结果先进"草稿 → 校验 → 保存（可撤销）"。
4. **修提醒链路**（P0-6/7/8/9）：通知 id 与实体 id 绑定、实现重复与预提醒、纠正 Snooze 语义、给 NagMode 加终止条件。
5. **设置持久化收口**（P1-9/P1-10）：`AppSettings` 全面落 Preferences 并在启动回读；语言切换后重建 Shell。
6. **存储换 SQLite**（P2-1）+ **补测试工程**（P2-2）。
7. **对齐文档**（P2-5）：更新 `build-status.md` / `models.md` / `development-plan.md`，使文档与代码一致。
