---
name: voicetodo-maui-dev-plan
overview: 为 d:/FromGit/todolist 从零搭建一个 .NET MAUI 语音待办 + 临时定时器 App。先产出本地 docs（市场/竞品调研、Reddit 功能缺口、开源模型可行性），再产出开发计划，采用「Core 业务/AI 内核 + MAUI 壳」分层架构，初始化即纳入多国多语言（i18n）与端侧离线（隐私优先）设计。
todos:
  - id: create-research-docs
    content: 创建 docs/research 三个调研文档（市场/Reddit/开源模型）
    status: completed
  - id: create-dev-plan-doc
    content: 编写 docs/plan/development-plan.md（Core+MAUI 壳架构与 i18n 里程碑）
    status: completed
    dependencies:
      - create-research-docs
  - id: scaffold-core
    content: 搭建 VoiceTodo.Core 类库（模型/抽象接口/管线/存储/i18n 资源）
    status: completed
    dependencies:
      - create-dev-plan-doc
  - id: scaffold-maui-shell
    content: 搭建 VoiceTodo.Maui 壳（工程/DI/中英文 RESX/主页面/平台服务）
    status: completed
    dependencies:
      - create-dev-plan-doc
  - id: implement-voice-pipeline
    content: 集成 sherpa-onnx STT/TTS（Core 服务 + MAUI 平台绑定）
    status: completed
    dependencies:
      - scaffold-core
      - scaffold-maui-shell
  - id: implement-nlu-notifications
    content: 实现规则 NLU + 中文时间解析 + Plugin.LocalNotification 提醒/定时器
    status: completed
    dependencies:
      - scaffold-core
---

## 用户需求

在当前空目录 `d:/FromGit/todolist` 中创建一个 .NET MAUI App，内置端侧语音模型/意图识别，本质是一个语音待办 + 临时定时器应用。核心交互示例：「举杠铃20分钟」「5分钟后催促我开启举杠铃」。

## 产品概述

一个隐私优先、完全离线运行的语音生产力 App：用户通过语音创建待办事项（Todo）与临时定时器（Timer），并支持健身/家务等免提场景。差异化定位为「语音建待办 + 语音建定时器 + 健身免提」三者合一且端侧不传云。

## 核心功能（已吸纳 Reddit 调研 A/B/C/D）

- A 提醒可靠性：Nag Mode（重复催促直到确认）、Snooze/自动延后/预提醒、自定义与语音播报提醒音。
- B 计时灵活度：可变间歇序列（多段 work/rest）、语音引导阶段切换、背景音乐不中断、预设模板（Tabata/HIIT/Circuit）。
- C 语义自然度：相对时间口语解析（后天/周四3点/5分钟后/每天周一）、多语言。
- D 隐私本地化：端侧语音识别与合成，不依赖云端。

## 文档与架构交付要求

1. 将前期联网调研内容落地到本地 `docs/`（市场竞品、Reddit 功能缺口、开源模型可行性）。
2. 将开发计划写入 `docs/plan/development-plan.md`，按 **Core（无 UI 领域层）+ MAUI 壳（UI/平台服务）** 方式组织。
3. 初始化即考虑多国多语言（中/英 RESX + Culture 提供器，TTS 按 locale 选嗓音）。

## 技术栈选型

- 框架：.NET 9/10 + .NET MAUI（单 C# 代码库跨 Android/iOS/Windows/MacCatalyst）。
- 语音识别与合成：**sherpa-onnx**（`org.k2fsa.sherpa.onnx`，ASR+TTS 一体，移动端一等支持；中文优先 SenseVoice/Paraformer，备选 whisper.net）。
- 意图与时间解析：规则 NLU（正则/关键词，离线、稳定，MVP 采用）+ 自写中文时间解析器（英文走 nChronic/ChronicNetCore）。
- 提醒/定时器：Plugin.LocalNotification（跨平台、定时、自定义铃声、通知按钮、周期重复，支撑 Nag/Snooze）。
- 持久化：SQLite（sqlite-net-pcl 或 EF Core）。
- 多语言：.NET RESX 资源 + CultureInfo 提供器，中/英从初始化起支持；Core 与 MAUI 各自 RESX，TTS 按 locale 选嗓音。

## 实现方案

采用 **Core + MAUI 壳**分层：Core 为不依赖 MAUI 的纯 .NET 类库（领域模型、AI 管线编排、存储、i18n 资源与本地化接口），保证逻辑可测试、可复用、与平台解耦；MAUI 壳负责 UI、页面、依赖注入，并通过平台服务实现 Core 定义的抽象接口（如 `SherpaOnnxSpeechRecognizer`、`LocalNotificationScheduler`）。

数据流：用户语音 → `ISpeechRecognizer`（sherpa-onnx STT）→ 文本 → `VoicePipeline` 编排 → `IIntentParser`（规则分类 Todo/Timer）+ `ITimeParser`（中文/英文相对时间）→ 统一 `VoiceCommand` 实体 → `ITodoRepository`（SQLite 落库）与 `INotificationScheduler`（Plugin.LocalNotification 预约提醒/定时器）→ `ITextToSpeech`（sherpa-onnx TTS）语音反馈。

关键技术决策：

- 选 sherpa-onnx 而非 whisper.net，因其 STT+TTS 一体、移动端支持最完整、模型生态覆盖 Whisper/Moonshine/SenseVoice/Paraformer，减少依赖数量（符合最小依赖原则）。
- MVP 用规则 NLU 而非端侧 SLM：Phi-3 等 SLM 经 ONNX Runtime GenAI 在移动端支持尚不成熟，规则方案离线、确定性强、零模型体积，SLM 列为后续可选增强。
- i18n 在 Core 与 MAUI 双层各自 RESX：Core 负责解析提示/语音短语本地化，MAUI 负责 UI 文案，App 启动按设备或用户偏好设置 `CultureInfo`。

性能与可靠性：sherpa-onnx 在移动端借助 CoreML/GPU delegate 近实时；规则 NLU 为 O(1) 正则匹配，时间解析为 O(n) 线性扫描；通知调度交由 OS，避免应用后台保活。模型体积：Whisper tiny ~75MB、SenseVoice-small 数十 MB、Piper/Kokoro TTS <100MB，适配手机存储。

## 实现要点（防回归）

- Core 不得引用任何 MAUI/平台程序集，保持纯 .NET Standard/类库，确保可单测与未来复用（如转桌面/CLI）。
- 平台服务（STT/TTS/通知）严格实现 Core 抽象接口，DI 在 `MauiProgram.cs` 中按平台注册，避免 UI 直接耦合原生 API。
- 通知与定时器共享 `ReminderSettings`（NagMode/Snooze/预提醒），避免两套调度逻辑。
- i18n 文案集中 RESX，TTS 嗓音按 `CultureInfo` 选择，新增语言只需补 RESX + 对应 TTS 模型，无需改逻辑。

## 架构设计

```mermaid
flowchart TD
    U[用户语音] --> STT[ISpeechRecognizer / sherpa-onnx]
    STT --> TXT[文本]
    TXT --> NLP[VoicePipeline 编排]
    NLP --> IP[IIntentParser: RuleBasedIntentParser]
    NLP --> TP[ITimeParser: ChineseTimeParser / EnglishTimeParser]
    IP --> ENT[VoiceCommand 实体: Todo | Timer]
    TP --> ENT
    ENT --> REP[ITodoRepository: SQLite]
    ENT --> SCH[INotificationScheduler: Plugin.LocalNotification]
    SCH --> NOTIF[本地通知 / NagMode / Snooze / 定时器]
    REP --> UI[MAUI 页面 / ViewModel]
    TTS[ITextToSpeech / sherpa-onnx] --> OUT[语音反馈]
    LOC[ILocalizer + RESX] --> NLP
    LOC --> UI
    LOC --> TTS
```

Core 与 MAUI 壳边界清晰：Core 含领域模型、抽象接口、管线服务、规则解析、存储接口与本地化资源；MAUI 壳含页面、ViewModel、DI、平台服务实现与 UI 资源。

## 目录结构

```
d:/FromGit/todolist/
├── docs/
│   ├── research/
│   │   ├── market-and-competitors.md   # [NEW] 市场与竞品调研：语音待办赛道拥挤、语音定时器聚焦健身、结合类空白、端侧差异化机会
│   │   ├── reddit-feature-gaps.md       # [NEW] Reddit 功能缺口：NagMode/Snooze/语音播报、可变间歇+语音引导+背景音乐、相对时间+多语、本地化强诉求
│   │   └── open-source-models.md        # [NEW] 开源模型可行性：sherpa-onnx/whisper.net STT、sherpa-onnx TTS、规则NLU+中文时间解析、Plugin.LocalNotification、SQLite，逐项映射 A/B/C/D
│   └── plan/
│       └── development-plan.md          # [NEW] 开发计划：Core+MAUI 壳架构、i18n 初始化、里程碑、模块拆分、选型定案
├── src/
│   ├── VoiceTodo.Core/                  # [NEW] 纯 .NET 类库，无 MAUI 依赖
│   │   ├── VoiceTodo.Core.csproj        # [NEW] 类库工程，引用 SQLite/规则解析，不引 MAUI
│   │   ├── Models/
│   │   │   ├── TodoItem.cs              # [NEW] 待办实体（标题/完成态/截止/重复规则）
│   │   │   ├── TimerItem.cs             # [NEW] 临时定时器实体（含间歇阶段序列、触发时间）
│   │   │   ├── IntervalPhase.cs         # [NEW] 间歇阶段（类型 work/rest、时长、轮次）
│   │   │   ├── VoiceCommand.cs          # [NEW] 解析后统一意图实体（Type=Todo|Timer + 字段）
│   │   │   └── ReminderSettings.cs      # [NEW] NagMode/Snooze/预提醒/自定义铃声配置
│   │   ├── Abstractions/
│   │   │   ├── ISpeechRecognizer.cs     # [NEW] 语音识别抽象
│   │   │   ├── ITextToSpeech.cs         # [NEW] 语音合成抽象
│   │   │   ├── IIntentParser.cs         # [NEW] 意图分类抽象
│   │   │   ├── ITimeParser.cs           # [NEW] 相对时间解析抽象（含 CultureInfo）
│   │   │   ├── INotificationScheduler.cs # [NEW] 通知/定时器调度抽象
│   │   │   ├── ITodoRepository.cs       # [NEW] 本地存储抽象
│   │   │   └── ILocalizer.cs            # [NEW] 本地化抽象
│   │   ├── Services/
│   │   │   ├── VoicePipeline.cs         # [NEW] 编排 STT→解析→实体→落库/调度
│   │   │   ├── RuleBasedIntentParser.cs # [NEW] 关键词/正则意图分类（Todo/Timer）
│   │   │   ├── ChineseTimeParser.cs     # [NEW] 中文相对时间解析（分钟/小时/天/周/后天/每天周一）
│   │   │   ├── EnglishTimeParser.cs     # [NEW] 包装 nChronic 的英文时间解析
│   │   │   └── TodoRepository.cs        # [NEW] SQLite 实现（ITodoRepository）
│   │   └── Resources/
│   │       ├── CoreStrings.resx         # [NEW] 英文资源（解析提示/语音短语）
│   │       └── CoreStrings.zh.resx      # [NEW] 中文资源
│   └── VoiceTodo.Maui/                  # [NEW] MAUI 应用壳
│       ├── VoiceTodo.Maui.csproj        # [NEW] MAUI 工程，引用 Core + sherpa-onnx + Plugin.LocalNotification
│       ├── MauiProgram.cs               # [NEW] DI 注册平台服务与 Core 服务
│       ├── App.xaml / App.xaml.cs       # [NEW] 启动设置 CultureInfo（设备/用户偏好）
│       ├── Platforms/                   # [NEW] Android/iOS/Windows 平台服务实现与权限配置
│       ├── Services/
│       │   ├── SherpaOnnxSpeechRecognizer.cs # [NEW] 实现 ISpeechRecognizer（sherpa-onnx）
│       │   ├── SherpaOnnxTextToSpeech.cs     # [NEW] 实现 ITextToSpeech（sherpa-onnx，按 locale 选嗓音）
│       │   └── LocalNotificationScheduler.cs # [NEW] 实现 INotificationScheduler（Plugin.LocalNotification，含 Nag/Snooze）
│       ├── Resources/
│       │   ├── AppResources.resx        # [NEW] 英文 UI 文案
│       │   └── AppResources.zh.resx     # [NEW] 中文 UI 文案
│       ├── ViewModels/
│       │   └── MainViewModel.cs         # [NEW] 主视图模型（语音指令/列表绑定）
│       └── Pages/
│           ├── MainPage.xaml/.cs        # [NEW] 主页面：语音按钮 + 待办/定时器列表
│           ├── TimerPage.xaml/.cs       # [NEW] 定时器页面：间歇序列/预设模板/语音引导
│           └── SettingsPage.xaml/.cs    # [NEW] 设置页：语言、NagMode、TTS 嗓音
```

## 关键代码结构

```
public interface ISpeechRecognizer { Task<string> RecognizeAsync(CancellationToken ct); }
public interface IIntentParser { VoiceCommand Parse(string text, CultureInfo culture); }
public interface ITimeParser { DateTimeOffset? ParseRelative(string text, CultureInfo culture); }
public interface INotificationScheduler { Task ScheduleAsync(VoiceCommand cmd, ReminderSettings settings); }
public interface ITextToSpeech { Task SpeakAsync(string text, CultureInfo culture); }
```