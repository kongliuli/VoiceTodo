# 开发计划 — VoiceTodo（语音待办 + 临时定时器）

> 规划时间：2026-09-10
> 架构原则：**Core（无 UI 领域层）+ MAUI 壳（UI/平台服务）** 分层；**初始化即多国多语言（i18n）**；端侧离线、隐私优先。

## 1. 产品定位

隐私优先、完全离线运行的语音生产力 App：通过语音创建**待办事项（Todo）**与**临时定时器（Timer）**，覆盖健身/家务等免提场景。差异点 = 语音建待办 + 语音建定时器 + 健身免提三者合一 + 端侧不传云。

核心交互示例：
- 「举杠铃 20 分钟」→ 创建一个 20 分钟计时器（可含语音阶段引导）。
- 「5 分钟后催促我开启举杠铃」→ 5 分钟后触发提醒（Nag Mode 直到确认）。

## 2. 已吸纳的增强功能（A/B/C/D）

- **A 提醒可靠性**：Nag Mode、Snooze/自动延后/预提醒、自定义与语音播报提醒音。
- **B 计时灵活度**：可变间歇序列、语音引导阶段切换、背景音乐不中断、预设模板（Tabata/HIIT/Circuit）。
- **C 语义自然度**：相对时间口语（后天/周四3点/5分钟后/每天周一）、多语言。
- **D 隐私本地化**：端侧语音识别与合成，不依赖云端。

## 3. 技术栈选型

| 层 | 选型 |
|---|---|
| 框架 | .NET 9/10 + .NET MAUI（单 C# 代码库跨 Android/iOS/Windows/MacCatalyst） |
| 语音识别/合成 | **sherpa-onnx**（`org.k2fsa.sherpa.onnx`，ASR+TTS 一体；中文优先 SenseVoice/Paraformer；备选 whisper.net） |
| 意图/时间 | 规则 NLU（正则/关键词，离线稳定，MVP）+ 自写**中文时间解析器**（英文走 nChronic） |
| 提醒/定时器 | **Plugin.LocalNotification**（定时/自定义铃声/通知按钮/周期重复 → Nag/Snooze） |
| 持久化 | **SQLite**（sqlite-net-pcl 或 EF Core） |
| 多语言 | .NET RESX + CultureInfo 提供器，中/英起步，TTS 按 locale 选嗓音 |

### 关键技术决策

1. **选 sherpa-onnx 而非 whisper.net**：STT+TTS 一体、移动端支持最完整、模型生态覆盖 Whisper/Moonshine/SenseVoice/Paraformer，依赖更少（最小依赖原则）。
2. **MVP 用规则 NLU 而非端侧 SLM**：Phi-3 等 SLM 经 ONNX Runtime GenAI 在移动端尚不成熟；规则方案离线、确定性强、零模型体积，SLM 列为后续可选增强。
3. **i18n 双层 RESX**：Core 负责解析提示/语音短语本地化，MAUI 负责 UI 文案；App 启动按设备或用户偏好设置 `CultureInfo`。

## 4. 架构：Core + MAUI 壳

```mermaid
flowchart TD
    U[用户语音] --> STT[ISpeechRecognizer / sherpa-onnx]
    STT --> TXT[文本]
    TXT --> NLP[VoicePipeline 编排]
    NLP --> IP[IIntentParser: RuleBasedIntentParser]
    NLP --> TP[ITimeParser: Chinese/EnglishTimeParser]
    IP --> ENT[VoiceCommand: Todo | Timer]
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

### Core（纯 .NET 类库，无 MAUI 依赖）
- 领域模型：`TodoItem`、`TimerItem`、`IntervalPhase`、`VoiceCommand`、`ReminderSettings`
- 抽象接口：`ISpeechRecognizer`、`ITextToSpeech`、`IIntentParser`、`ITimeParser`、`INotificationScheduler`、`ITodoRepository`、`ILocalizer`
- 服务：`VoicePipeline`（编排）、`RuleBasedIntentParser`、`ChineseTimeParser`、`EnglishTimeParser`（包 nChronic）、`TodoRepository`（SQLite）
- 资源：`CoreStrings.resx` / `CoreStrings.zh.resx`

### MAUI 壳（UI / 平台服务）
- `MauiProgram.cs`：DI 按平台注册平台服务与 Core 服务
- `App.xaml(.cs)`：启动设置 `CultureInfo`
- 平台服务：`SherpaOnnxSpeechRecognizer`、`SherpaOnnxTextToSpeech`（按 locale 选嗓音）、`LocalNotificationScheduler`（含 Nag/Snooze）
- UI 资源：`AppResources.resx` / `AppResources.zh.resx`
- 页面：`MainPage`（语音按钮 + 列表）、`TimerPage`（间歇/模板/语音引导）、`SettingsPage`（语言/NagMode/TTS 嗓音）
- ViewModel：`MainViewModel`

### 数据流
用户语音 → `ISpeechRecognizer`(sherpa-onnx STT) → 文本 → `VoicePipeline` → `IIntentParser`(规则) + `ITimeParser`(中/英相对时间) → `VoiceCommand` → `ITodoRepository`(SQLite 落库) 与 `INotificationScheduler`(预约提醒/定时器) → `ITextToSpeech`(语音反馈)。

## 5. 目录结构

```
d:/FromGit/todolist/
├── docs/
│   ├── research/{market-and-competitors,reddit-feature-gaps,open-source-models}.md
│   └── plan/development-plan.md
├── src/
│   ├── VoiceTodo.Core/            # 纯 .NET 类库，无 MAUI 依赖
│   │   ├── VoiceTodo.Core.csproj
│   │   ├── Models/{TodoItem,TimerItem,IntervalPhase,VoiceCommand,ReminderSettings}.cs
│   │   ├── Abstractions/{ISpeechRecognizer,ITextToSpeech,IIntentParser,ITimeParser,INotificationScheduler,ITodoRepository,ILocalizer}.cs
│   │   ├── Services/{VoicePipeline,RuleBasedIntentParser,ChineseTimeParser,EnglishTimeParser,TodoRepository}.cs
│   │   └── Resources/{CoreStrings.resx,CoreStrings.zh.resx}
│   └── VoiceTodo.Maui/            # MAUI 应用壳
│       ├── VoiceTodo.Maui.csproj
│       ├── MauiProgram.cs
│       ├── App.xaml(.cs)
│       ├── Platforms/             # Android/iOS/Windows 平台服务实现与权限配置
│       ├── Services/{SherpaOnnxSpeechRecognizer,SherpaOnnxTextToSpeech,LocalNotificationScheduler}.cs
│       ├── Resources/{AppResources.resx,AppResources.zh.resx}
│       ├── ViewModels/MainViewModel.cs
│       └── Pages/{MainPage,TimerPage,SettingsPage}.xaml(.cs)
```

## 6. 关键接口契约

```csharp
public interface ISpeechRecognizer { Task<string> RecognizeAsync(CancellationToken ct); }
public interface IIntentParser { VoiceCommand Parse(string text, CultureInfo culture); }
public interface ITimeParser { DateTimeOffset? ParseRelative(string text, CultureInfo culture); }
public interface INotificationScheduler { Task ScheduleAsync(VoiceCommand cmd, ReminderSettings settings); }
public interface ITextToSpeech { Task SpeakAsync(string text, CultureInfo culture); }
```

## 7. 里程碑

| 阶段 | 内容 | 产出 |
|---|---|---|
| M0 文档 | 调研 + 本计划（已完成） | docs/ |
| M1 脚手架 | 创建解决方与圆 + Core 类库 + MAUI 壳骨架 + i18n 双语 RESX | 可编译空壳 |
| M2 语音管线 | 集成 sherpa-onnx STT/TTS（Core 服务 + MAUI 绑定） | 能听、能说 |
| M3 理解层 | 规则 NLU + 中文时间解析 + nChronic 英文 | 语音→结构化命令 |
| M4 调度层 | Plugin.LocalNotification 提醒/定时器 + Nag/Snooze + 定时器引擎 + 语音阶段引导 | 端到端可用 |
| M5 丰富度 | A/B/C/D 增强（可变间歇、预设模板、背景音乐共存、场景模式、TTS 嗓音选择） | 完整产品 |
| M6（可选） | 端侧 SLM 增强意图理解（Phi-3/LLamaSharp） | 更强泛化 |

## 8. 防回归要点

- Core **不得**引用任何 MAUI/平台程序集，保持纯 .NET，确保可单测与未来复用（桌面/CLI）。
- 平台服务严格实现 Core 抽象接口，DI 在 `MauiProgram.cs` 按平台注册，避免 UI 直接耦合原生 API。
- 通知与定时器共享 `ReminderSettings`（NagMode/Snooze/预提醒），避免两套调度逻辑。
- i18n 文案集中 RESX，TTS 嗓音按 `CultureInfo` 选择；新增语言只需补 RESX + 对应 TTS 模型，不改逻辑。

## 9. 性能与可靠性

- sherpa-onnx 移动端借 CoreML/GPU delegate 近实时。
- 规则 NLU 为 O(1) 正则匹配；时间解析为 O(n) 线性扫描。
- 通知调度交 OS，避免应用后台保活（iOS 后台语音受限，用本地通知 + 常驻前台/Widget 兜底）。
- 模型体积：Whisper tiny ~75MB、SenseVoice-small 数十 MB、Piper/Kokoro TTS <100MB。
