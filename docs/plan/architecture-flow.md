# VoiceTodo 实现链路与工作流

> **[已合并]** 本文内容已并入 `voicetodo-master.md`（第二、三部分）。保留本文件仅作历史备查，后续请以总纲为准。
>
> 整理时间：2026-09-11
> 用途：说明「一次语音指令从进到出经过哪些环节」以及「这个项目是怎么被开发和产出的」。
> 配套：缺漏清单见 `gap-analysis.md`（已冻结为待办，本文只描述现状，不含修复方案）。

---

## 一、工程结构（两个工程、一条边界）

| 工程 | 职责 | 关键约束 |
|---|---|---|
| `src/VoiceTodo.Core` | 领域模型、抽象接口、编排服务、规则解析、本地化资源 | **不得引用任何 MAUI / 平台程序集**（纯 net10.0），保证可单测、可复用 |
| `src/VoiceTodo.Maui` | 页面、ViewModel、DI、平台服务实现、UI 资源 | 只通过 Core 的抽象接口与之交互 |

边界由三组东西锁定：
1. `Core/Abstractions/*` —— 9 个接口（识别、合成、意图、时间、通知、仓库、本地化、模型、模型种类）；
2. `MauiProgram.CreateMauiApp()` —— 唯一在按平台选择实现的地方（`#if ANDROID`）；
3. `Core` 的 csproj —— 无任何 `PackageReference`。

---

## 二、运行时实现链路（一次语音指令的完整旅程）

七步，顺序执行：

| # | 环节 | 类型 / 文件 | 现状 |
|---|---|---|---|
| 1 | 拾音 | `IMicrophoneCapture` → `DemoMicrophoneCapture` | **桩**：返回应用数据目录下的静音 WAV（1 秒 16kHz），无平台实现 |
| 2 | 语音识别 | `WhisperSpeechRecognizer`（whisper.net） | 固定 8 秒；去 44 字节 RIFF 头后按 16kHz 原始 PCM 送 whisper；模型目录由 `IModelProvider` 解析 |
| 3 | 管线编排 | `VoicePipeline.ProcessTextAsync` | 解析 → 按 `Action` 分派 `Add / Complete / Delete / Query`；失败播报 `UnknownCommand` |
| 4 | 意图 + 时间解析 | `RuleBasedIntentParser` + `CompositeTimeParser`(中/英) | 关键词判定 Todo/Timer 与动作；提取时长、相对时间、重复规则；**不产出间歇阶段** |
| 5 | 落库 | `JsonFileTodoRepository` | 写入 `%LocalAppData%/VoiceTodo/voicetodo.json`，自增 `NextId`；仅写操作加信号量 |
| 6 | 排提醒 | `LocalNotificationScheduler`(Android) / `WindowsNotificationScheduler` | 按 `TriggerAt` 或 `now + Duration` 排通知；应用 `NagMode` / `Snooze` |
| 7 | 反馈与刷新 | `NativeTextToSpeech` → `MainViewModel.RefreshAsync` | 平台原生 TTS 播报结果；VM 重新读仓库并重建列表 |

补充：`VoicePipeline` 通过构造参数 `Func<ReminderSettings>` 拿到全局默认提醒（由 `AppSettings.SceneDefaults` 按免提场景给出），因此场景切换会改变后续新建项的策略。

## 三、三条支线链路

**待办支线**：`MainViewModel.ListenAsync / AddTextAsync` → `VoicePipeline` → `JsonFileTodoRepository` → `RefreshAsync` 分三区渲染（今日 / 已完成 / 待安排计数）。
行内交互：`TodoRowVm.ToggleDoneCommand` → `MarkDoneAsync`；`DeleteCommand` → `DeleteTodoAsync` + 从 `ObservableCollection` 移除。

**计时支线**（与语音支线解耦）：`TimerPage` 的预设/常用计时/最近使用 → 展开成扁平 `SchedItem` 计划 → `RunLoopAsync` 每 250ms 推进 → 每秒 `RunningTimerHub.Publish(快照)` → `MainPage` 订阅 `RunningTimerHub.Changed` 显示「正在计时」卡。
暂停 = 冻结 `RemainingWhenPaused`；继续 = 以冻结值重算 `EndAt`；自然结束播报 `TimerDone` 后 `Publish(null)`。
**注意：这条链路只活在内存，不落库、不排通知。**

**通知支线**：`INotificationScheduler.ScheduleAsync` / `CancelAsync`。Android 由 `Plugin.LocalNotification` 承载并挂 `NotificationActionTapped`（用于「完成」动作回查），Windows 走 WinAppSDK toast。

## 四、模型资源链路（工具链工作流）

```
manifest.json（候选目录，唯一事实源）
   └─ download-models.ps1 ──► ModelsLibrary/<relpath>/     （全量并存，不进 git）
          └─ models.pack.json（本次要打进包里的 Id 列表）
                 └─ pack-models.ps1 ──► Resources/Models/<relpath>/   （MauiAsset）
                        └─ 运行时 ──► AppData/Models/<relpath>/      （首次提取后交给 whisper）
```

- `manifest.json` 同时被 Core 的 `ModelManifest`/`ModelEntry`、下载脚本、打包脚本三处消费；
- 每个模型目录带 `.filelist`（递归清单），运行时按它整棵提取；
- `ModelProvider` 先试 `FileSystem.OpenAppPackageFileAsync`，失败再回退到 `AppContext.BaseDirectory` 磁盘路径（兼容 Windows unpackaged）。

## 五、用户使用工作流（用户视角）

> 本节描述「用户拿到 App 之后实际怎么操作」，按屏幕与操作路径组织，不涉及内部实现。

### 5.0 导航骨架

- 底部三个主 Tab：**今天**（MainPage）· **清单**（ListsPage）· **计时**（TimerPage）
- 子页（push 进入）：**设置**（SettingsPage）· **日历**（CalendarPage）
- **语音是全局操作，不占导航目的地**——它固定在「今天」页底部

### 5.1 语音记一条待办（核心路径）

| 步 | 用户动作 | 界面反应 |
|---|---|---|
| 1 | 「今天」页点底部大按钮「对我说…」 | 按钮禁用，文案切成状态文字（倾听中 / 识别中…） |
| 2 | 免提说话 | 管线运行：拾音→识别→解析→落库→排提醒 |
| 3 | 松手 / 等待 | TTS 播报结果（已添加 / 没听清） |
| 4 | 看列表 | 新待办出现在「每日待办」区，顶部摘要计数 +1 |

失败分支：识别为空或无法解析 → TTS 播报「未知命令」，**不落库**，且**没有确认 / 重试界面**（缺口）。

### 5.2 打字添加（同一管线的另一半）

底部「打字添加…」→ 弹系统输入框 → 确定 → 走 `AddTextAsync`（跳过识别，其余环节一致）；取消或空输入无操作。

### 5.3 完成 / 撤销完成

- 行左侧空心圆圈 → `ToggleDoneCommand`：标题变灰 + 删除线，移入「已完成」折叠区
- 「已完成 N」卡片 → 展开 / 收起该区
- ⚠️ 行尾的 `›` 目前**没有任何手势绑定**，点它不会进入详情或编辑（缺口）

### 5.4 查找与筛选

| 入口 | 落点 |
|---|---|
| 「今天」页右上 🔍 | `ListsPage`（默认筛选＝待安排） |
| 「每日待办」右侧「查看全部 ›」 | `ListsPage?filter=today` |
| 首页「待安排」收件箱卡 | `ListsPage?filter=unscheduled` |

`ListsPage` 内：顶部搜索框（标题即时包含匹配）+ 四个筛选胶囊（待安排 / 今天 / 未来 / 已完成），列表按 `DueAt` 升序。
⚠️ 该页**没有「新建」入口**（缺口）。

### 5.5 日历

「今天」页顶部工具条「打开日历」→ `CalendarPage`：`‹ ›` 换区间 · 「今天」回跳 · 月 / 周切换 · 点某天 → 下方列当天待办；页底另有「待安排」收容区可展开。

### 5.6 计时（与语音链路解耦的独立通路）

- Tab 到「计时」→ 两个分段：**倒计时** / **间歇训练**
- 倒计时：点 5 / 10 / 25 分钟卡片**直接开跑**；或从「最近使用」点 ▶ 复跑
- 间歇：切到「间歇训练」→ 右上 `+` 展开预设 → Tabata(20/10×8) / HIIT(30/15×8) / Circuit(45/15×6) → 开始
- 运行视图：大环形进度 + 中央剩余 + 「接下来」提示 + 暂停 / 继续 + 结束（二次确认）
- **跨页继续**：回到「今天」页，顶部出现琥珀色「正在计时」卡（剩余 / 总时长 / 进度）；点卡片跳回计时页
- 自然结束 → TTS 播报「计时完成」→ 卡片消失

⚠️ 计时**不落库、不排通知**：锁屏或杀进程即丢，也不会在后台响铃（缺口）。

### 5.7 设置

「今天」页右上 ⚙ → `SettingsPage`，四组：

1. **通用**：语言、ASR 模型（切换即写 `Preferences`）
2. **声音与提醒**：自定义铃声、TTS 嗓音、Nag 模式、Snooze、预提醒
3. **使用体验**：动效开关、免提场景、音频共存（播报压低而非暂停 BGM）
4. **权限与隐私**：麦克风 / 通知（只读展示）

改动即时生效。⚠️ 其中「免提场景 / Snooze / 预提醒 / 自定义铃声 / 动效」落在 `AppSettings` 静态字段，**重启即丢**（缺口）。

### 5.8 提醒闭环

待办到点 → 系统通知 → 点通知打开应用；Android 通知可带「完成」动作回填。
⚠️ 通知 id 未与待办主键绑定，取消 / 更新会错位；Windows 侧通知动作未接（缺口）。

## 六、数据与状态落在哪里

| 内容 | 位置 | 是否持久 |
|---|---|---|
| 待办 / 定时器 | `%LocalAppData%/VoiceTodo/voicetodo.json` | ✅ |
| 激活模型、语言、NagMode、音频共存、TTS 嗓音 | `Preferences` | ✅ |
| 免提场景、Snooze、预提醒、自定义铃声、动效开关 | `AppSettings` 静态字段 | ❌ 重启即丢 |
| 运行中的计时 | `TimerPage` 字段 + `RunningTimerHub` 静态快照 | ❌ 进程结束即丢 |
| 模型文件 | `ModelsLibrary/`（仓库）→ `Resources/Models/`（包内）→ `AppData/Models/`（运行时） | ✅ |

---

## 附：开发侧工作流（非用户视角）

> 上一版误把这条当成「工作流」，用户澄清指的是**用户使用的工作流**（见第五节）。此处保留备查。

```
联网调研 ─► docs/research/*        （竞品、Reddit 功能缺口、开源模型可行性）
   └─► docs/plan/development-plan.md   （架构与里程碑 M0–M6）
          └─► docs/design/maui/         （设计母版 + 14 屏底图 + 生成提示词）
                 └─► 按屏实现 Pages/ + ViewModels/ + Services/
```

产出与验证：`dotnet build` 两个 TFM（android / windows）→ 侧载安装 → 真机验证（尚未进行）。
