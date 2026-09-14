# VoiceTodo 总纲：现状 · 链路 · 方向

> 整理时间：2026-09-11（12:45 合并三份文档；同日下午追加**第七部分**：用户严苛自查 + 联网调研补充）
> **最近更新：2026-09-14** —— A1–A5（静默失效）与 C1/C2/C4/C5/C11/C12（缺失功能）已实现并闭合，详见 [1330/08-open-gaps.md](1330/08-open-gaps.md) §G；§1.3 的 Windows 警告数已复测为 43。
> 本文合并了三份独立文档：
> - `gap-analysis.md`（现状盘点与缺漏清单）
> - `architecture-flow.md`（实现链路 + 用户使用工作流）
> - `resident-voice-direction.md`（产品方向：常驻语音交互）
>
> 配套：`page-interaction.md`（11 个页面的逐页功能、跳转图、跨页通道，以及第九/十节的用户视角自查与调研）。
>
> 定位：**项目单一事实来源**。查现状、查链路、查缺口、查方向、查用户体验风险，都以本文为准。

---

## 目录

- [第一部分 项目概览](#第一部分-项目概览)
- [第二部分 实现链路](#第二部分-实现链路)
- [第三部分 用户使用工作流](#第三部分-用户使用工作流)
- [第四部分 现状盘点与缺漏](#第四部分-现状盘点与缺漏)
- [第五部分 产品方向：常驻语音交互](#第五部分-产品方向常驻语音交互)
- [第六部分 待办清单与推进顺序](#第六部分-待办清单与推进顺序)
- [第七部分 用户严苛自查与调研补充](#第七部分-用户严苛自查与调研补充)
- [附录 A 开发侧工作流](#附录-a-开发侧工作流)
- [附录 B 并发修改与文档漂移记录](#附录-b-并发修改与文档漂移记录)

---

# 第一部分 项目概览

## 1.1 项目是什么

| 项 | 内容 |
|---|---|
| 产品 | 隐私优先、端侧离线的语音待办 + 临时定时器 App（健身 / 家务 / 开车免提） |
| 形态 | .NET 10 + .NET MAUI，目标 `net10.0-android` + `net10.0-windows10.0.19041.0` |
| 架构 | `VoiceTodo.Core`（纯 .NET 领域层，无 MAUI 依赖）+ `VoiceTodo.Maui`（UI / 平台服务） |
| ASR | whisper.net（whisper.cpp），模型随包分发 + 首次提取到 AppData |
| TTS | 平台原生（Android `TextToSpeech` / Windows `SpeechSynthesizer`） |
| NLU | 规则 / 关键词 + 自写中英时间解析器（离线、零模型） |
| 存储 | JSON 文件（`%LocalAppData%/VoiceTodo/voicetodo.json`） |
| 通知 | Android 走 `Plugin.LocalNotification 13.0.0`；Windows 走 WinAppSDK toast |

## 1.2 工程结构与分层边界

| 工程 | 职责 | 关键约束 |
|---|---|---|
| `src/VoiceTodo.Core` | 领域模型、抽象接口、编排服务、规则解析、本地化资源 | **不得引用任何 MAUI / 平台程序集**（纯 net10.0），保证可单测、可复用 |
| `src/VoiceTodo.Maui` | 页面、ViewModel、DI、平台服务实现、UI 资源 | 只通过 Core 的抽象接口与之交互 |

边界由三组东西锁定：

1. `Core/Abstractions/*` —— 9 个接口（识别、合成、意图、时间、通知、仓库、本地化、模型、模型种类）；
2. `MauiProgram.CreateMauiApp()` —— 唯一在按平台选择实现的地方（`#if ANDROID`）；
3. `Core` 的 csproj —— 无任何 `PackageReference`。

## 1.3 构建状态（2026-09-14 复核）

环境：dotnet SDK `10.0.401`；workload android `36.1.69`、maui-windows `10.0.20`、ios / maccatalyst 均已安装。

| 目标 | 结果（2026-09-14 实测） |
|---|---|
| `VoiceTodo.Core` (net10.0) | 通过（1 警告 CS8670：`ModelEntry.Options` 初始化项可能隐式引用 null 成员） |
| `VoiceTodo.Maui` (net10.0-windows10.0.19041.0) | **通过 · 0 错误 / 43 警告**（2026-09-14 下午复测；早前记录为 34 条，本轮新增代码后增加，未逐条比对来源） |
| `VoiceTodo.Maui` (net10.0-android) | **通过 · 0 错误 / 42 警告** |
| Android Release APK | 通过（98.8 MB，`arm64-v8a` + `x86_64`，已签名，含 `whisper-tiny` 模型） |
| 回归自检 `tools/VoiceTodo.SelfCheck` | **23/23 通过** |

> 下两节（1.3.1 / 1.3.2）保留为**历史留档**：记录 2026-09-11 重构中途（约 10:56）的失败现场与当时的判断。
> 其中 E1–E3 与 CA1416 的**结论均已复核**，见各表「现状」列与 1.3.2 的按语。**表中 3 个错误当前已不再复现。**

### 1.3.1 阻塞构建的 3 个错误（重构中新引入）

| # | 位置 | 错误 | 原因与修法 | 现状 |
|---|---|---|---|---|
| E1 | `Pages/MainPage.xaml.cs:37-38` | CS1061：`CollectionView` 未包含 `CollectionChanged` | `CollectionView` 不是可观察集合。应改为订阅 ViewModel 暴露的 `ObservableCollection`（`Todos` / `DoneTodos`），或用 `(TodoList.ItemsSource as INotifyCollectionChanged)?.CollectionChanged` | **代码已改为订阅 `_vm.Todos` / `_vm.DoneTodos` 的 `CollectionChanged`（`MainPage.xaml.cs:41-42`）** |
| E2 | `Pages/TimerPage.xaml.cs:276` | CS8852：只能对 `init` 属性赋值 | `RunningTimerInfo.Title` 是 `{ get; init; }`，而 `BeginPhase()` 在构造后 `_info.Title = _runTitle;`。改为 `{ get; set; }`，或每阶段 `Publish` 一个新实例 | **`RunningTimerInfo.Title` 已改为 `{ get; set; }`（`RunningTimerHub.cs:8`）** |
| E3 | 同上 | （同因）`RunningTimerInfo` 是共享可变实例，`Title` 只在构造时赋值 | 见 E2 | **随 E2 一并解决** |

> **状态（2026-09-14 复核）**：三个错误**均已修复，且已通过双 TFM 构建确认**（Windows / Android 各 0 错误）。原 P0-1「当前不能编译」正式**关闭**。

### 1.3.2 非阻塞但值得修的告警

| 类别 | 位置 | 说明 |
|---|---|---|
| CS0618 | `TimerPage.xaml.cs:400` | `DisplayAlert` 已过时 → 用 `DisplayAlertAsync` |
| CS8600/8601/8602/8604/8618 | `PlatformServices/AudioCoexist.cs` 多处 | 空值分析：`_audioManager` 未用 `required`，`AudioAttributes` 链式调用可能为 null |
| **CA1416** | `AudioCoexist.cs:31/46/52` | `AudioFocusRequestClass` 需要 Android API 26+，但工程 `SupportedOSPlatformVersion` 为 24.0。**复核结论（2026-09-14）：不会运行时崩溃** —— 焦点请求的构造包在 `try/catch` 内（`AudioCoexist.cs:25-40`），API 24/25 上抛出的错误被吞掉、`_focusRequest = null`，`DuckForSpeech / Restore` 随即退化为空操作。真实后果是「Android 7.x 上背景音不会被压低」，**不是崩溃**。若需消除告警，可加 `#if ANDROID26_0_OR_GREATER` 回退旧 `RequestAudioFocus` 重载，或把最低版本提到 26 |

> 补充：10:36 之前（重构开始前）两个目标都是**可以编译通过**的。

> **按语（2026-09-14）**：当前告警构成已复核 —— Windows 34 条 / Android 42 条，分类为 CS0618（`DisplayAlert` / `DisplayActionSheet` 过时）、CA1416 / CA1422（Android API 级别）、CS860x（可空性）、XC0022（未指定 `x:DataType` 的绑定）、CS8670（`ModelEntry.Options`），**全部非阻断**。CS0618 属 MAUI 10 的 API 更名（`DisplayAlert` → `DisplayAlertAsync`），可择机批量替换。

---

# 第二部分 实现链路

## 2.1 一次语音指令的七步旅程

七步，顺序执行：

| # | 环节 | 类型 / 文件 | 现状 |
|---|---|---|---|
| 1 | 拾音 | `IMicrophoneCapture` → `DemoMicrophoneCapture` | **桩**：返回应用数据目录下的静音 WAV（1 秒 16kHz），无平台实现 |
| 2 | 语音识别 | `WhisperSpeechRecognizer`（whisper.net） | 固定 8 秒；去 44 字节 RIFF 头后按 16kHz 原始 PCM 送 whisper；模型目录由 `IModelProvider` 解析 |
| 3 | 管线编排 | `VoicePipeline.ProcessTextAsync` | 解析 → 按 `Action` 分派 `Add / Complete / Delete / Query`；失败播报 `UnknownCommand` |
| 4 | 意图 + 时间解析 | `RuleBasedIntentParser` + `CompositeTimeParser`(中 / 英) | 关键词判定 Todo / Timer 与动作；提取时长、相对时间、重复规则；**不产出间歇阶段** |
| 5 | 落库 | `JsonFileTodoRepository` | 写入 `%LocalAppData%/VoiceTodo/voicetodo.json`，自增 `NextId`；仅写操作加信号量 |
| 6 | 排提醒 | `LocalNotificationScheduler`(Android) / `WindowsNotificationScheduler` | 按 `TriggerAt` 或 `now + Duration` 排通知；应用 `NagMode` / `Snooze` |
| 7 | 反馈与刷新 | `NativeTextToSpeech` → `MainViewModel.RefreshAsync` | 平台原生 TTS 播报结果；VM 重新读仓库并重建列表 |

补充：`VoicePipeline` 通过构造参数 `Func<ReminderSettings>` 拿到全局默认提醒（由 `AppSettings.SceneDefaults` 按免提场景给出），因此场景切换会改变后续新建项的策略。

## 2.2 三条支线链路

**待办支线**：`MainViewModel.ListenAsync / AddTextAsync` → `VoicePipeline` → `JsonFileTodoRepository` → `RefreshAsync` 分三区渲染（今日 / 已完成 / 待安排计数）。
行内交互：`TodoRowVm.ToggleDoneCommand` → `MarkDoneAsync`；`DeleteCommand` → `DeleteTodoAsync` + 从 `ObservableCollection` 移除。

**计时支线**（与语音支线解耦）：`TimerPage` 的预设 / 常用计时 / 最近使用 → 展开成扁平 `SchedItem` 计划 → `RunLoopAsync` 每 250ms 推进 → 每秒 `RunningTimerHub.Publish(快照)` → `MainPage` 订阅 `RunningTimerHub.Changed` 显示「正在计时」卡。
暂停 = 冻结 `RemainingWhenPaused`；继续 = 以冻结值重算 `EndAt`；自然结束播报 `TimerDone` 后 `Publish(null)`。
**注意：这条链路只活在内存，不落库、不排通知。**

**通知支线**：`INotificationScheduler.ScheduleAsync` / `CancelAsync`。Android 由 `Plugin.LocalNotification` 承载并挂 `NotificationActionTapped`（用于「完成」动作回查），Windows 走 WinAppSDK toast。

### 2.2.1 三处「半截通路」（数据结构已备好，只差接线）

1. `VoiceCommand.Phases` 与 `TimerItem.Phases` **已定义**、`MapTimer` 也**已在传**——但 `RuleBasedIntentParser` **从不填充它**，所以「八组、每组三十秒、休息十秒」现在一个字都解析不出来。
2. `VoicePipeline.AddAsync` 遇到 `Type == Timer` 只做「写库 + 排通知」，**从不交给计时运行引擎**——语音说「计时十分钟」只会多一条记录，界面上不会真的开始倒计时。
3. 反过来，运行引擎本身（`SchedItem` 摊平轮次 + `RunningTimerHub` 跨页快照 + 暂停 / 续跑）**已写完、能跑**，只是目前只被 TimerPage 的手动点击驱动。

## 2.3 模型资源链路（工具链）

```
manifest.json（候选目录，唯一事实源）
   └─ download-models.ps1 ──► ModelsLibrary/<relpath>/     （全量并存，不进 git）
          └─ models.pack.json（本次要打进包里的 Id 列表）
                 └─ pack-models.ps1 ──► Resources/Models/<relpath>/   （MauiAsset）
                        └─ 运行时 ──► AppData/Models/<relpath>/      （首次提取后交给 whisper）
```

- `manifest.json` 同时被 Core 的 `ModelManifest` / `ModelEntry`、下载脚本、打包脚本三处消费；
- 每个模型目录带 `.filelist`（递归清单），运行时按它整棵提取；
- `ModelProvider` 先试 `FileSystem.OpenAppPackageFileAsync`，失败再回退到 `AppContext.BaseDirectory` 磁盘路径（兼容 Windows unpackaged）。

## 2.4 数据与状态落在哪里

| 内容 | 位置 | 是否持久 |
|---|---|---|
| 待办 / 定时器 | `%LocalAppData%/VoiceTodo/voicetodo.json` | 是 |
| 激活模型、语言、NagMode、音频共存、TTS 嗓音 | `Preferences` | 是 |
| 免提场景、Snooze、预提醒、自定义铃声、动效开关 | `AppSettings` 静态字段 | **否（重启即丢）** |
| 运行中的计时 | `TimerPage` 字段 + `RunningTimerHub` 静态快照 | **否（进程结束即丢）** |
| 模型文件 | `ModelsLibrary/`（仓库）→ `Resources/Models/`（包内）→ `AppData/Models/`（运行时） | 是 |

---

# 第三部分 用户使用工作流

> 本节描述「用户拿到 App 之后实际怎么操作」，按屏幕与操作路径组织，不涉及内部实现。

## 3.0 导航骨架

- 底部三个主 Tab：**今天**（MainPage）· **清单**（ListsPage）· **计时**（TimerPage）
- 子页（push 进入）：**设置**（SettingsPage）· **日历**（CalendarPage）
- **语音是全局操作，不占导航目的地**——它固定在「今天」页底部

## 3.1 语音记一条待办（核心路径）

| 步 | 用户动作 | 界面反应 |
|---|---|---|
| 1 | 「今天」页点底部大按钮「对我说…」 | 按钮禁用，文案切成状态文字（倾听中 / 识别中…） |
| 2 | 免提说话 | 管线运行：拾音 → 识别 → 解析 → 落库 → 排提醒 |
| 3 | 松手 / 等待 | TTS 播报结果（已添加 / 没听清） |
| 4 | 看列表 | 新待办出现在「每日待办」区，顶部摘要计数 +1 |

失败分支：识别为空或无法解析 → TTS 播报「未知命令」，**不落库**，且**没有确认 / 重试界面**（缺口）。

## 3.2 打字添加（同一管线的另一半）

底部「打字添加…」→ 弹系统输入框 → 确定 → 走 `AddTextAsync`（跳过识别，其余环节一致）；取消或空输入无操作。

## 3.3 完成 / 撤销完成

- 行左侧空心圆圈 → `ToggleDoneCommand`：标题变灰 + 删除线，移入「已完成」折叠区
- 「已完成 N」卡片 → 展开 / 收起该区
- 行尾的 `›` 目前**没有任何手势绑定**，点它不会进入详情或编辑（缺口）

## 3.4 查找与筛选

| 入口 | 落点 |
|---|---|
| 「今天」页右上放大镜 | `ListsPage`（默认筛选＝待安排） |
| 「每日待办」右侧「查看全部 ›」 | `ListsPage?filter=today` |
| 首页「待安排」收件箱卡 | `ListsPage?filter=unscheduled` |

`ListsPage` 内：顶部搜索框（标题即时包含匹配）+ 四个筛选胶囊（待安排 / 今天 / 未来 / 已完成），列表按 `DueAt` 升序。
该页**没有「新建」入口**（缺口）。

## 3.5 日历

「今天」页顶部工具条「打开日历」→ `CalendarPage`：`‹ ›` 换区间 · 「今天」回跳 · 月 / 周切换 · 点某天 → 下方列当天待办；页底另有「待安排」收容区可展开。

## 3.6 计时（与语音链路解耦的独立通路）

- Tab 到「计时」→ 两个分段：**倒计时** / **间歇训练**
- 倒计时：点 5 / 10 / 25 分钟卡片**直接开跑**；或从「最近使用」点播放复跑
- 间歇：切到「间歇训练」→ 右上 `+` 展开预设 → Tabata(20/10×8) / HIIT(30/15×8) / Circuit(45/15×6) → 开始
- 运行视图：大环形进度 + 中央剩余 + 「接下来」提示 + 暂停 / 继续 + 结束（二次确认）
- **跨页继续**：回到「今天」页，顶部出现琥珀色「正在计时」卡（剩余 / 总时长 / 进度）；点卡片跳回计时页
- 自然结束 → TTS 播报「计时完成」→ 卡片消失

计时**不落库、不排通知**：锁屏或杀进程即丢，也不会在后台响铃（缺口）。

## 3.7 设置

「今天」页右上齿轮 → `SettingsPage`，四组：

1. **通用**：语言、ASR 模型（切换即写 `Preferences`）
2. **声音与提醒**：自定义铃声、TTS 嗓音、Nag 模式、Snooze、预提醒
3. **使用体验**：动效开关、免提场景、音频共存（播报压低而非暂停 BGM）
4. **权限与隐私**：麦克风 / 通知（只读展示）

改动即时生效。其中「免提场景 / Snooze / 预提醒 / 自定义铃声 / 动效」落在 `AppSettings` 静态字段，**重启即丢**（缺口）。

## 3.8 提醒闭环

待办到点 → 系统通知 → 点通知打开应用；Android 通知可带「完成」动作回填。
通知 id 未与待办主键绑定，取消 / 更新会错位；Windows 侧通知动作未接（缺口）。

---

# 第四部分 现状盘点与缺漏

## 4.1 对照 14 屏设计母版

`docs/design/maui/screens-v1/` 定义了 14 张目标页面。下面是**新开发内容落地后**的对照（12:45 快照，详表见 `page-interaction.md` 第八节）：

| # | 设计屏 | 状态 | 说明 |
|---|---|---|---|
| 01 | 今天（首页） | 已实现 | 摘要行、运行计时卡（500ms 心跳）、今日安排、已完成折叠、待安排入口、查看全部；**新增**：行点击进编辑、自动到期提醒、Debug 种子 |
| 02 | 清单（待安排 / 今天 / 未来 / 已完成 + 搜索） | 已实现 | `ListsPage`，四段筛选 chip + 即时搜索 + 空态文案；**新增**行点击进编辑；仍无「新建」入口 |
| 03 | 任务编辑 | **已实现** | `TaskEditorPage`（`id=N`）：标题 / 日期 / 时间 / 提醒 / 重复 / 备注 + 删除；仓库补 `UpdateTodoAsync` |
| 04 | 语音聆听 | 已实现 | `VoiceCapturePage`：波形、60s 上限、手动完成、改用打字 |
| 05 | 识别确认 | **已实现** | 确认页展示原文 + 解析草稿，均可点开覆盖层修改；计时草稿走「先确认再投放」 |
| 06 | 文字添加 | 已实现 | 独立页 `TextAddPage`：300ms 防抖解析 + chips + 覆盖层修改 |
| 07 | 计时列表（多计时器） | 部分 | 有倒计时 / 间歇分段、常用计时、最近使用；仍为**单个**运行计时 |
| 08 | 间歇配置 | **已实现** | `TimerEditorPage`：Tabata / HIIT / 自定义模板 + 步进器 + 轮次预览 |
| 09 | 计时运行 | 已实现 | 大数字、环形进度、轮次、接下来、语音提示；结束确认 |
| 10 | 计时暂停 | 已实现 | 暂停冻结剩余、继续按冻结值重算；跨页面继续运行 |
| 11 | 到期提醒（应用内响应） | **已实现** | `ReminderPage`：完成 / 延后（5 / 10 / 30 分或自定义时刻） |
| 12 | 设置 | 部分 | 四分组、语言即时生效；权限仍**只读展示**（无申请动作） |
| 13 | 首次使用 / 权限引导 | **已实现** | `OnboardingPage`（置 `onboarding.done`）；但**未申请任何权限** |
| 14 | 日历与当日安排 | 部分 | 月 / 周视图 + 当日待办 + **计时记录图层**（可改名）；待办行**不可点** |

统计变化：**未实现 4 → 0；部分 5 → 4**（计时列表、日历、设置权限、间歇预设重复）。

### 4.1.1 因新页面而改判的原 P0 条目

| 原条目 | 新状态 |
|---|---|
| P0-1 不能编译 | **代码已修**，改判「待构建确认」（见 1.3.1） |
| P0-4 无法编辑已有待办 | **已解决**：`TaskEditorPage` + `UpdateTodoAsync`；但日历页待办行仍不可点（I-1） |
| P0-5 识别结果不可确认 | **已解决**：`VoiceCapturePage` 确认态 + 草稿可改 |
| P0-2 / P0-3 / P0-6~P0-10 | **仍未解决**（麦克风桩、权限不申请、提醒链路四条） |

> 其余 P1 / P2 条目**依然有效**；第四部分的 P0 表除上表已改判者外，整体仍然成立。

## 4.2 P0 — 阻断核心价值 / 当前不可用

> ⚠️ **本节 4.2 / 4.3 / 4.4 三张表已过期**（写于 2026-09-11 重构中途），**请勿再作为待办依据**。
> 逐条复核结论：
> - **完全闭合**：P0-1、P0-2、P0-5、P0-6、P0-8、P0-9；P1-1、P1-2、P1-6、P1-9、P1-10。
> - **仍开放**：**P0-3**（通知权限未申请）→ 08 A1。
> - **部分闭合**：**P0-4** 编辑主通路已通、日历行仍不可点 → 08 A3；**P0-7** 的「无终止条件」已闭合（`NagPolicy` 有上限与取消路径），但「按任务停止催促」仍缺 → 08 A4。
> - **改判为平台局限**：**P0-10** Windows 端已改为「如实不谎报」（`SupportsFutureScheduling=false`），能力本身受 WindowsAppSDK 限制 → 见 06 §7.1 L2。
>
> **当前有效的开放缺口清单见 [1330/08-open-gaps.md](1330/08-open-gaps.md)** —— 含通知权限、常驻会话外围、并发训练无保护、录音无清理、无障碍为零、i18n 未收口、TTS 嗓音死设置等。

| # | 缺漏 | 证据 | 影响 |
|---|---|---|---|
| P0-1 | ~~当前不能编译~~ **代码已修，待构建确认**（E1/E2/E3 见 1.3.1） | 见 1.3.1 | 一次 `dotnet build` 即可确认，不再视为阻塞 |
| P0-2 | **没有真实录音**：麦克风是桩，永远返回静音 WAV | `PlatformServices/DemoMicrophoneCapture.cs`；`MauiProgram.cs:55` 无条件注册 Demo；`IMicrophoneCapture` 无任何平台实现 | 语音输入形同虚设，识别恒为空 |
| P0-3 | **无权限申请流程**：只「查看」不「请求」 | `SettingsPage.LoadPermissionStatusAsync` 仅调 `Permissions.CheckStatusAsync`，全仓无 `RequestAsync` | Android 13+ 录音 / 通知被拒且无法引导授权 |
| P0-4 | ~~无法编辑已有待办~~ **已解决**（`TaskEditorPage` + `UpdateTodoAsync`）；**残留**：日历页待办行不可点 | `page-interaction.md` I-1 | 编辑主通路已通，日历入口仍断 |
| P0-5 | ~~识别结果不可确认~~ **已解决**（`VoiceCapturePage` 确认态 + 草稿可改） | 设计 05 已落地 | 误识别可当场修正 |
| P0-6 | **提醒取消失效（id 错位）** | 调度器自增 `_nextId`（进程内）vs 仓库持久化 `Id`；`CancelAsync(match.Id)` 传的是仓库 id；重启后必然错位 | 删除 / 完成待办后提醒仍会响 |
| P0-7 | **NagMode 无法停止** | `LocalNotificationScheduler.ScheduleAsync`：1 分钟周期重复，无终止条件 | 开启后持续打扰，且取消不掉 |
| P0-8 | **重复任务不生效** | `RecurrenceRule` 只存不排，无任何消费方 | 「每天 / 每周一」不会重复提醒 |
| P0-9 | **预提醒未实现；Snooze 语义用错** | `PreAlert` 无消费方；`ScheduleAsync` 把 Snooze 当作「首触发时间整体后移」 | 配了 Snooze 会让**所有**提醒晚点响 |
| P0-10 | **Windows 端提醒残缺** | `WindowsNotificationScheduler`：无按钮、无 Nag / Snooze、`CancelAsync` 空实现 | 桌面端提醒不可靠 |

## 4.3 P1 — 核心功能不完整

| # | 缺漏 | 说明 |
|---|---|---|
| P1-1 | 语音无法生成可变间歇序列 | `VoiceCommand.Phases` 始终为 `null`，解析器从不填充 |
| P1-2 | 无自定义间歇编辑器 | 无法输入 work / rest / 轮次（设计 08） |
| P1-3 | 无多计时器并行 | `RunningTimerHub` 明示「MVP 只承载单个活动计时」（设计 07） |
| P1-4 | 无应用内到期提醒响应 | 无完成 / 延后按钮闭环（设计 11） |
| P1-5 | 无首次使用 / 权限引导 | 设计 13 |
| P1-6 | 计时不落库、不排通知 | 退出应用即丢，与其他页面 / 通知无联动 |
| P1-7 | **TTS 嗓音设置是死设置** | `Preferences["ttsVoice"]` 写入后**无人读取**；`docs/models.md` 承诺的 TTS 模型下拉框也不存在 |
| P1-8 | 非随包模型不可用 | 无运行时下载；设置里能选但提取不到文件，静默失败 |
| P1-9 | **部分设置不持久化** | `Scene` / `Snooze` / `PreAlert` / `CustomSoundPath` / `UseAnimations` 仍是内存态，重启即丢 |
| P1-10 | 启动不应用已保存语言 | `App.xaml.cs` 用 `CultureInfo.CurrentUICulture`，不读 `Preferences["lang"]`；只有进设置页才生效 |
| P1-11 | i18n 覆盖不全 | 硬编码中文：设置页「预提醒」「背景音乐共存」、`MainPage.FormatDuration` 的单位、语音按钮等；切语言不刷新已渲染页面 |
| P1-12 | 识别质量 / 健壮性 | 固定 8 秒；写死 16kHz 与 44 字节偏移；无 VAD / 流式；忽略 manifest 的 `ModelFile`；整段 PCM 全读入内存 |
| P1-13 | 不是「免提」 | 每次必须点按钮，无连续监听 / 唤醒词 / 硬件键，与开车 / 健身免提定位矛盾 |
| P1-14 | 空识别结果静默失败 | `MainViewModel.ListenAsync` 对空文本无任何反馈 |

## 4.4 P2 — 工程化 / 体验 / 文档

| # | 缺漏 | 说明 |
|---|---|---|
| P2-1 | 存储是 JSON 而非 SQLite | 与计划不符；每次操作全量读写、读操作无并发保护、无索引、无迁移 |
| P2-2 | **无任何测试** | 计划宣称 Core「可单测」；sln 仅 2 个项目，零测试工程、零 CI |
| P2-3 | 无 README / 无版本号 / 无关于页 / 无隐私说明 | 隐私是核心卖点却无对外声明 |
| P2-4 | 平台覆盖缩水 | 计划称四平台，csproj 只有 Android + Windows |
| P2-5 | **文档与代码不同步** | 详见附录 B |
| P2-6 | 无解析结果可视化 | 研究文档要求「语音原文 + 解析结果可视化」；只显示原文 |
| P2-7 | 无删除撤销 / 无导出导入备份 | — |
| P2-8 | `AsyncCommand` 异常不可观测 | `Execute` 为 `async void`；`CanExecuteChanged` 从不触发 |
| P2-9 | 无障碍未落实 | 设计规范要求 48dp 触控目标、字体缩放；导航图标用纯文字，未见验证 |
| P2-10 | 无日志 / 诊断 | 大量 `catch { }` 静默降级（通知、TTS、权限、模型提取），故障不可定位 |
| P2-11 | 残留临时产物 | `src/VoiceTodo.Maui/build.log`（调试遗留） |
| P2-12 | 细节问题 | `AudioCoexist` 用 `Media` usage 抢焦点播 TTS，语义可商榷；`NativeTextToSpeech` Windows 侧用 `text.Length/12` 估算播报时长，长文本会被截断 |

---

# 第五部分 产品方向：常驻语音交互

## 5.1 构想（用户 11:35 口述）

1. **常驻语音交互**：开启之后持续开着，通过**会话**持续对待办与闹钟做增删改查。
2. **即时任务**：可以创建「即时性的闹钟 / 倒计时组」——即健身那一类，`总计 x 组、持续 y 秒、休息 z 秒`。
3. **「非常驻」指的是「语音 / 会话的启动」**：不是开机就常开监听，而是**按需手动唤起**；唤起之后会话本身是常驻的，直到手动关闭（见 D1）。
4. **语音创建的健身定时器要留痕**：写入记录、进日历，可改名留档。

一句话提炼：**启动是按需的（非常驻），会话是持续的（常驻），记录是沉淀的（留痕）。**

## 5.2 术语澄清

| 概念 | 指什么 | 含义 |
|---|---|---|
| **非常驻** | 语音 / 会话的**启动** | 按需手动唤起，不是开机常开的监听服务 |
| **常驻** | 唤起之后的**会话运行期** | 后台持续，仅手动关闭（D1） |
| **留痕** | **通过语音创建的健身定时器** | 写入记录、进日历、可改名留档 |

> 这是**两条独立的轴**：启动轴（会话怎么起）与数据轴（生成的定时器怎么存），两者互不推导。

## 5.3 两层模型

| | **L1 常驻会话层** | **L2 语音创建的即时任务** |
|---|---|---|
| 启动方式 | **非常驻**：按需手动唤起，不是开机常开 | 依附 L1，会话里说一句就生成 |
| 运行期 | **常驻**：唤起后后台持续，仅手动关闭 | 单次，跑完结束 |
| 承载内容 | 待办 / 闹钟的增删改查 | 健身倒计时组（x 组 / 持续 y / 休息 z） |
| 状态 | 需要**上下文**（最近操作对象、指代） | **无状态**，创建即执行 |
| 落库 | 是（待办、闹钟是长期实体） | **留痕**：写入记录、进日历、可改名 |
| 与 UI 的关系 | 全局能力，不占导航位 | 会话产物，确认后直接进运行态 |
| 现状 | 无（单次命令，无状态，每次都要按） | 无（`Phases` 从不填充、不接运行引擎） |

**关键含义**：现状是「点按钮 → 固定 8 秒 → 一句命令」。与目标的差别集中在三处：

1. **启动**：现在每次都要按一下；目标是一次手动开启后持续可用，直到手动关。
2. **生成**：现在解析器产不出间歇结构；目标说一句「八组、每组三十秒、休息十秒」就成型。
3. **沉淀**：现在完全不落库；目标跑完写入一条记录，进日历。

## 5.4 差距清单

### L1 常驻会话层缺什么

| 能力 | 现状 | 目标 |
|---|---|---|
| 拾音 | 单次、**固定 8 秒**、按一下说一句 | 连续流 + 端点检测（VAD），说停即停 |
| 会话状态 | `ProcessTextAsync` **完全无状态** | Session 对象：最近实体、待补槽位、澄清栈 |
| 多轮 | 不存在 | 缺槽位就追问（「什么时候？」） |
| 指代 | 不存在 | 「删掉**它**」/「**那个**改成明天」需上下文 |
| 歧义 | 标题**子串取首个**，可能误删 | 候选列表 + 语音确认（「找到 2 个，是哪个？」） |
| 打断 | 无 | 播报中可插话（barge-in） |
| 资源 | 无 | 麦克风常驻、前台服务、电量 / 发热、音频焦点 |
| 抽象 | 已有 9 个接口 | 需新增会话类抽象（如 `ISpeechSession` / `IEndpointDetector`） |

### L2 语音创建的即时任务缺什么

| 能力 | 现状 | 目标 |
|---|---|---|
| 间歇解析 | `RuleBasedIntentParser` **从不填 `Phases`** | 解析「x 组 / 持续 y / 休息 z」→ `IntervalPhase[]` |
| 数据结构 | `VoiceCommand.Phases`、`TimerItem.Phases` **已存在** | 已备好，缺解析器接上（半截通路） |
| 落地执行 | `VoicePipeline.AddAsync` 对 Timer 只 `AddTimerAsync` + `ScheduleAsync` | 需要**交给运行引擎直接开跑**（半截通路） |
| 持久化 | Timer 一律落库，但**不接运行引擎** | **跑完留痕**（健身定时器记录 → 日历），并直连运行引擎 |

## 5.5 语音语法设计：即时倒计时组

### 用户给的模板

> 总计 x 组、持续 y 秒、休息 z 秒

### 需要覆盖的自然语言变体

| 说法 | 解析结果 |
|---|---|
| 做八组，每组三十秒，休息十秒 | phases=[work 30s, rest 10s], rounds=8 |
| 间歇训练，8 轮，20 秒运动 15 秒休息 | phases=[work 20s, rest 15s], rounds=8 |
| hitt 20/10 × 8 | phases=[work 20s, rest 10s], rounds=8 |
| 三组平板支撑，每组一分钟，歇半分钟 | phases=[work 60s, rest 30s], rounds=3 |
| 五分钟倒计时 | phases=[countdown 300s], rounds=1 |

### 解析产物

- 填充 `VoiceCommand.Phases`（**当前空缺的槽位**）
- `Type = Timer`、`Action = Add`
- 需要中文字数词与量词支持：「三十秒」「半分钟」「八组」

### 已在代码里备好的地基（可直接复用）

- `IntervalPhase`（`Kind` / `Duration` / `Rounds`）—— 已定义
- `TimerPage.SchedItem` 展开逻辑（`work` / `rest` 轮次摊平成扁平计划）—— 已实现且能跑
- `RunningTimerHub` 跨页快照 —— 已实现

也就是说：L2 缺的只有**三段接缝**——① 解析（补 `Phases`）② 确认应答 ③ 留痕落库 + 直连运行引擎。运行引擎本身是现成的。

## 5.6 留痕模型（健身定时器记录）

新增一个「运行记录」实体（暂名 `TimerSession`）：

| 字段 | 含义 |
|---|---|
| `Id` | 主键 |
| `Title` | 可后改（「八组燃脂」→「腿部训练 A」） |
| `StartedAt` / `EndedAt` | 起止时间，用于日历定位 |
| `Phases` + `Rounds` | 当次结构快照（work / rest / 轮次） |
| `Outcome` | 自然跑完 / 中途结束 |

落点：与待办、定时器同一个 json（或分表）；**`CalendarPage` 增加一个图层**显示当天的倒计时记录。

## 5.7 对架构的冲击面

| 层 | 影响 | 说明 |
|---|---|---|
| `Core/Abstractions` | 新增 | 会话、端点检测等抽象 |
| `Core/Services` | 改写 | `VoicePipeline` 由「单次函数」升级为「会话驱动器」 |
| `RuleBasedIntentParser` | 扩写 | 补 `Phases` 解析（间歇语法） |
| `ITimeParser` | 扩写 | 需支持「三十秒」「半分钟」等中文数词与量词 |
| `ITodoRepository` | 扩展 | 新增「运行记录」（`TimerSession`）的读写 |
| `MauiProgram` | 新增注册 | 会话服务 + 平台前台服务 / 权限 |
| Android 平台层 | 新增 | 前台服务（Foreground Service）+ 麦克风常驻 + 通知常驻 |
| `TimerPage` | 改接入 | 需接收会话产出的即时任务并直接进运行态；预设 chip 去留待 D4 |

## 5.8 已拍板决策（2026-09-11）

| # | 问题 | 决定 | 含义 |
|---|---|---|---|
| D1 | 常驻的形态 | **手动开启 → 后台常驻 → 仅手动关闭** | 不需要唤醒词；需要**前台服务 + 常驻通知**；**无空闲超时** |
| D2 | 是否留痕 | **通过语音创建的健身定时器全部留痕**，进日历、可改名 | 与「启动非常驻」不矛盾：非常驻指**启动**，留痕指**记录**（见 5.2） |
| D3 | 语音建组之后 | **先确认再跑** | 需要一次性确认应答（「开始吗」→ 开始 / 取消） |
| D4 | 现存 3 个预设 chip | **待定** | 预设 chip 与语音创建是**两个并行入口**，不互斥 |

## 5.9 演进步骤

1. **L2 主链路**（小、独立、体验见效快）
   ① `RuleBasedIntentParser` 补 `Phases` 解析（「x 组 / 持续 y / 休息 z」）
   ② 加**一次性确认应答**（D3：播报「八组，30 秒运动 10 秒休息，开始吗」）
   ③ 开跑 + **留痕**：写 `TimerSession` → 直连 `RunningTimerHub`
   ④ `CalendarPage` 加倒计时记录图层 + 记录可改名
   → 本步**不依赖任何常驻能力**，可先落地，立刻验证「说一句就开始练」。

2. **L1 地基**（手动开关 + 后台常驻）
   真实录音（替换 `DemoMicrophoneCapture` 桩）→ 运行时权限 → Android 前台服务 + 常驻通知 → 手动开 / 手动关的状态机。

3. **会话状态**
   Session 对象（最近实体、待补槽位、澄清栈）+ 槽位追问 + 歧义候选确认。

4. **真常驻体验打磨**
   连续拾音 + 端点检测（VAD）+ 播报中可插话（barge-in）。
   （**唤醒词不做**——D1 已选手动开启。）

---

# 第六部分 待办清单与推进顺序

## 6.1 冻结中的 17 条（按工作流分组，暂不动手）

| # | 区域 | 条目 |
|---|---|---|
| 1 | 构建 | 修复当前编译错误与代码告警 |
| 2 | 语音输入 | 实现真实麦克风采集（Android / Windows） |
| 3 | 语音输入 | 权限申请流程与首次使用引导（设计 13） |
| 4 | 语音输入 | ASR 健壮性：时长 / 采样率 / VAD / 模型文件 / 内存 |
| 5 | NLU | 语音解析可变间歇序列（填充 `VoiceCommand.Phases`） |
| 6 | 交互 | 识别确认页（设计 05）与草稿式捕捉流程 |
| 7 | 交互 | 任务编辑（设计 03）与仓库 `UpdateAsync` |
| 8 | 提醒 | 通知 id 与实体 id 绑定，修正取消失效 |
| 9 | 提醒 | NagMode 终止、重复排期、预提醒、Snooze 语义纠正 |
| 10 | 提醒 | Windows 端通知能力补齐（按钮 / 动作 / Nag / 取消） |
| 11 | 计时 | 多计时器并行、自定义间歇编辑器、计时落库与通知联动 |
| 12 | 提醒 | 应用内到期提醒响应页（设计 11） |
| 13 | 设置 | `AppSettings` 持久化、启动回读语言、TTS 嗓音接通或移除 |
| 14 | 本地化 | 清理硬编码文案并让切换语言即时刷新 |
| 15 | 存储 | JSON 文件存储替换为 SQLite |
| 16 | 工程 | 新增测试工程与最小 CI |
| 17 | 文档 | 同步 `build-status.md` / `models.md` / `development-plan.md`，清理残留 |

## 6.2 本条方向新增的待办

- 会话状态模型（Session / 上下文 / 澄清栈）
- 端点检测（VAD）与连续拾音
- 歧义候选 + 语音确认
- 即时生成通路（`Phases` 由会话现算，直连运行引擎）
- **运行记录留痕**（`TimerSession` 实体 + 日历图层 + 记录改名）
- **一次性确认应答**（D3：「开始吗」→ 开始 / 取消）
- 前台服务与麦克风常驻（Android，手动开 / 手动关）
- 中文数词量词解析（三十秒 / 半分钟 / 八组）

### 优先级变动

| 原条目 | 变化 |
|---|---|
| 真实录音（`DemoMicrophoneCapture` 是桩） | 变成 L1 的**前置条件** |
| 运行时权限申请 | 同上 |
| 识别失败无出口 | 常驻会话下**必须有**（否则会话会哑掉） |
| 可变间歇解析（第 5 条） | 从「NLU 缺口」变成 **L2 的核心功能** |
| 计时不落库 / 不排通知 | **需改写**：按 D2 必须留痕；「不排通知」仍成立 |

## 6.3 建议推进顺序

1. **修 3 个编译错误（E1/E2）**，让 HEAD 回到可构建——后续一切验证的前提。
2. **L2 主链路**（解析 `Phases` → 确认应答 → 留痕 + 直连运行引擎 → 日历图层）。
3. **真实录音 + 权限申请**（P0-2 / P0-3）。
4. **识别确认页 + 任务编辑页**（设计 05 / 03，仓库补 `UpdateAsync`）。
5. **修提醒链路**（P0-6/7/8/9）。
6. **设置持久化收口**（P1-9 / P1-10）。
7. **会话状态 → 真常驻**（VAD、前台服务、barge-in）。
8. **存储换 SQLite + 补测试工程**。
9. **对齐文档**。

## 6.4 严苛自查与调研新增的候选条目（2026-09-11 下午）

> 来源：`page-interaction.md` 第九、十节。按主题归类。

| 组 | 候选条目 |
|---|---|
| 健身计时（生死线） | 计时前台服务 + 锁屏 / 通知控制；阶段切换震动（Haptic）；多计时并行；预设保存 / 导出导入 |
| 提醒体感 | 通知静默时段 + 每日汇总（根治骚扰）；Nag 可控（P0-7）；通知 id 与实体绑定（P0-6） |
| 数据安全 | 删除撤销 / 回收站；数据导出 / 导入 / 备份；JSON → SQLite |
| 捕捉入口 | 桌面小组件 / 通知栏快速添加 |
| 体验专业度 | 关键路径日志（替代 `catch { }`）；设置全量持久化；启动回读语言；无障碍；隐私说明页 |
| 平台合规 | FGS `type=microphone` + 可见时开启 + 常驻通知（对应 X-1 / X-2） |

---

# 第七部分 用户严苛自查与调研补充

> 本节是**摘要**，完整表格见 `page-interaction.md` 第九、十节。

## 7.1 一句话结论

当前版本**页面骨架已经很全（11 页 / 14 屏零未实现）**，但**三条"生命线"仍然断着**，任何一条都会让用户直接卸载：

1. **语音生命线**：麦克风是桩 + 权限不申请 → 核心卖点当场证伪。
2. **健身生命线**：计时不落库、无前台服务、无通知 → 锁屏即丢，健身场景不可用。
3. **信任生命线**：提醒删不掉 / Nag 停不掉 / Debug 灌 30 条假数据 → 用户失去信任。

## 7.2 严苛自查：19 条不满意点（分级）

| 级别 | 条目 | 编号 |
|---|---|---|
| 🔴 致命（装了立刻卸载） | 语音点了没反应；权限不申请；Debug 灌假数据；识别失败无解释 | U-1 ~ U-4 |
| 🟠 严重（用几天就烦） | 计时后台不可靠；提醒骚扰且甩不掉；日历行不可点；清单无新建；纠错成本高；识别慢；单计时；误删无回收 | U-5 ~ U-12 |
| 🟡 糙点（觉得不专业） | 设置不持久；语言不回读；i18n 硬编码；无隐私声明；无备份；无障碍缺失；无日志 | U-13 ~ U-19 |

## 7.3 与常驻方向的硬冲突（X 系列，必须先解）

| # | 冲突 | 一句话 |
|---|---|---|
| X-1 | Android 12+ 禁止后台启动前台服务 | 「开启」必须发生在 App 可见时 |
| X-2 | Android 14+ 校验 while-in-use 麦克风权限 | 后台建 FGS 会抛 `SecurityException`；后台录音 ~60s 上限 |
| X-3 | 常驻麦克风的隐私感知 | 必须有录音指示 + 一键停止 |
| X-4 | 会话要状态 vs 管线无状态 | 需新增 Session 对象 |
| X-5 | 常驻的电量 / 发热 / 音频焦点 | VAD 按需拾音 + `AudioCoexist` 压低 |

> **D1 的「后台常驻」在 Android 上是*有条件的常驻***：可见时开启 + 前台服务 + 常驻通知 + 麦克风类型声明，四件套缺一不可。

## 7.4 调研补充：从竞品与平台学到的（⬜ = 当前未做）

| 方向 | 用户真会要、但我们没写的 |
|---|---|
| 计时 / 健身 | ⬜ 后台可靠运行（第一诉求）· ⬜ 震动反馈 · 🟡 预设导出 · ⬜ 桌面组件快速开始 · ⬜ 多计时并行 |
| 待办 | ⬜ 桌面小组件 / 通知栏快速添加 · ⬜ 静默时段 + 每日汇总 · ✅ 离线 · ⬜ 导出备份 |
| 语音 | ✅ push-to-talk（合 D1）· ⬜ barge-in · 🟡 常驻录音指示 · ✅ 误识别纠错 |
| 平台 | FGS 启动限制 · while-in-use 权限 · 后台麦克风 60s · 通知 / 组件豁免路径 |
| 底座 | ⬜ 权限申请带说明 · ⬜ 空状态引导 · ⬜ 删除撤销 · ⬜ 关键路径日志 · ⬜ 隐私声明 |

## 7.5 对推进顺序的影响

在 6.3 的基础上，**优先级发生三处变化**：

1. 「计时落库 / 前台服务」从 P1-6 **上调为健身场景的生死线**，且与 L1 的前台服务**共用同一套 Android 能力** → 建议**与 L1 地基合并规划**。
2. 「权限申请」（P0-3）从「语音功能的前置」**升级为全 App 的前置**：通知、录音都依赖它。
3. 「提醒体感的骚扰问题」（P0-6/7）优先级**上调**：它同时是「待办类 UX 反感」调研里最致命的一类（用户会直接关掉通知 → 整个提醒功能失效）。

---

# 附录 A 开发侧工作流

> 注意：这**不是**用户使用的工作流（用户使用的工作流见第三部分）。

```
联网调研 ─► docs/research/*        （竞品、Reddit 功能缺口、开源模型可行性）
   └─► docs/plan/development-plan.md   （架构与里程碑 M0–M6）
          └─► docs/design/maui/         （设计母版 + 14 屏底图 + 生成提示词）
                 └─► 按屏实现 Pages/ + ViewModels/ + Services/
```

产出与验证：`dotnet build` 两个 TFM（android / windows）→ 侧载安装 → 真机验证（**尚未进行**）。

---

# 附录 B 并发修改与文档漂移记录

## B.1 分析期间项目被并发修改

本次盘点过程中，仓库源码被**另一个进程 / 会话持续改写**：

| 时间 | 文件 |
|---|---|
| 10:37:00 | `Services/RunningTimerHub.cs`（**新增**） |
| 10:46:56 → 10:47:29 | `Pages/SettingsPage.xaml(.cs)` |
| 10:48:40 → 10:49:38 | `Pages/CalendarPage.xaml(.cs)`、`Pages/ListsPage.xaml(.cs)`（**新增**） |
| 10:51:52 → 10:53:25 | `Pages/TimerPage.xaml(.cs)` |
| 10:53:46 | `Drawables/RingDrawable.cs` |
| 10:54:37 → 10:55:08 | `Pages/MainPage.xaml(.cs)` |
| 10:55:41 → 10:55:47 | `ViewModels/MainViewModel.cs`、`ViewModels/TodoRowVm.cs` |

这是一次**按 `docs/design/maui/` 设计母版 V1 落地的重构**（新增「清单」页、文字添加、计时运行 / 暂停、首页今日视图、设置分组）。

因此：**本文反映的是 10:56 之后的快照**；`Core` 层（`VoicePipeline` / 解析器 / 仓库 / 通知调度）在同一时段**未被修改**。

## B.2 已失效的文档结论

| 文档 | 失效内容 |
|---|---|
| `build-status.md` | 「Windows 被 WMC0001 卡死」「所有目标被 API 不匹配卡死」——**均已失效**（10:36 前两目标可编译） |
| `models.md` | 列的 SenseVoice / Paraformer / Kokoro 与 manifest 实际的 3 条 whisper 不符；承诺的 TTS 模型下拉框不存在 |
| `development-plan.md` | 计划中的 sherpa-onnx TTS 已换成原生 TTS；称四平台，实际只有 Android + Windows |
