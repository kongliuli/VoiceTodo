# VoiceTodo

**隐私优先、端侧离线的语音待办 + 健身间歇计时 App。**
语音识别在设备本地完成，数据只落在本机，不上传、不联网、无账号、无统计。

| 项 | 值 |
|---|---|
| 版本 | 1.1（`ApplicationDisplayVersion`，`ApplicationVersion` 2） |
| 目标平台 | Android（minSdk 24 / targetSdk 36）、Windows 10.0.19041+ |
| 应用 ID | `com.voicetodo.app` |
| 技术栈 | .NET 10 · .NET MAUI · C# |

> 当前**开放缺口**（含优先建议）见 [`docs/plan/1330/08-open-gaps.md`](docs/plan/1330/08-open-gaps.md)。
> 项目**总纲 / 单一事实来源**见 [`docs/plan/voicetodo-master.md`](docs/plan/voicetodo-master.md)。

---

## 1. 能做什么

| 能力 | 说明 |
|---|---|
| 语音记待办 | 说完即建；「五分钟后提醒我关火」→ 待办 + 提醒时刻（**不是**倒计时） |
| 口述训练计划 | 「深蹲10组，每组持续5秒钟，休息10秒」→ 解析成计划并**自动开跑** |
| 间歇计时 | 组数 / 每组时长 / 组间休息 / 末组休息 / 倒数窗 / 阶段播报 |
| 提醒闭环 | 完成 / 取消勾选 / 删除 / 延后 → 同步取消或重排系统通知；支持每天、每周重复 |
| 计时留痕 | 每次训练写入记录并进日历，可改名留档 |
| 端侧识别 | `whisper.net`（whisper.cpp 绑定），随包 `whisper-tiny` 多语模型，开箱可用 |
| 文字入口 | 与语音共用同一条解析管线，识别不准时可手动改草稿 |

## 2. 工程结构

```
VoiceTodo.sln
├─ src/VoiceTodo.Core/         领域层：模型 / 解析 / 仓库 / 调度决策（零第三方依赖，可离线单测）
├─ src/VoiceTodo.Maui/         MAUI 壳：页面 + 平台服务（双 TFM）
│   ├─ Pages/                  11 个 ContentPage
│   ├─ ViewModels/             MainViewModel / TodoRowVm
│   ├─ PlatformServices/       录音、通知调度、前台服务、音频共存、TTS
│   ├─ Services/               统一变更入口、跨页启动、会话状态机、常驻队列
│   └─ Resources/Models/       manifest.json + 随包模型（其余模型不进 git）
├─ tools/VoiceTodo.SelfCheck/  回归自检（纯 .NET、离线；**未加入 sln**，单独运行）
├─ scripts/                    download-models.ps1 / pack-models.ps1（模型拉取与打包）
├─ docs/                       设计 / 计划 / 调研（见下方导航）
└─ dist/                       本地打包产物（APK，不进 git）
```

**分层边界**由三处锁定：`Core/Abstractions/*` 的接口、`MauiProgram.CreateMauiApp()` 唯一的按平台选择点、`VoiceTodo.Core.csproj` 零 `PackageReference`。

## 3. 构建

```bash
# 双 TFM 全量构建
dotnet build src/VoiceTodo.Maui/VoiceTodo.Maui.csproj -f net10.0-windows10.0.19041.0
dotnet build src/VoiceTodo.Maui/VoiceTodo.Maui.csproj -f net10.0-android
```

要求：.NET SDK 10 及以上，已安装 `android` 与 `maui-windows` workload。

## 4. 打包 Android APK

```bash
dotnet publish src/VoiceTodo.Maui/VoiceTodo.Maui.csproj \
  -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

产物：`src/VoiceTodo.Maui/bin/Release/net10.0-android/com.voicetodo.app-Signed.apk`（约 99 MB，含 74 MB 语音模型）。

安装：`adb install -r <apk>`，或传到设备点击安装（需允许「安装未知来源应用」）。

> **两个已知点**
> 1. 包内仅含 `arm64-v8a` + `x86_64`，32 位 `armeabi-v7a` 老机装不上。
> 2. Release 全量 AOT 约 13 分钟。日常调试可加 `-p:AndroidEnableProfiledAot=false` 或 `-p:RuntimeIdentifiers=android-arm64` 显著提速。
> 3. 当前使用 **debug keystore** 签名（工程未配置发布签名），适合内部安装，**不可上架**。

## 5. 回归自检

```bash
dotnet run --project tools/VoiceTodo.SelfCheck/VoiceTodo.SelfCheck.csproj
```

纯 .NET、离线、无外部测试框架依赖；覆盖训练计划展开、中文口语时间解析、解析器边界、通知 ID 派生、Nag 策略、运行快照恢复、仓库损坏读与原子写、提醒调度决策。全部通过时退出码 0。

## 6. 文档导航

| 文档 | 用途 |
|---|---|
| [`docs/plan/voicetodo-master.md`](docs/plan/voicetodo-master.md) | **总纲 / 单一事实来源**：现状、链路、用户工作流、缺口、方向 |
| [`docs/plan/1330/08-open-gaps.md`](docs/plan/1330/08-open-gaps.md) | **当前开放缺口清单**（取代 master 第 4.2–4.4 节） |
| [`docs/plan/1330/06-execution-plan.md`](docs/plan/1330/06-execution-plan.md) | 实施计划、验收清单、§7 局限与遗留项 |
| [`docs/plan/1330/07-batch-completion-report.md`](docs/plan/1330/07-batch-completion-report.md) | 上一批修复的完成报告与构建证据 |
| [`docs/privacy.md`](docs/privacy.md) | 隐私说明：数据放在哪、用了什么权限 |
| `docs/design/maui/` | 设计母版与 14 屏参考 |
| `docs/research/` | 竞品、模型、命名等调研 |

## 7. 数据与隐私（摘要）

所有数据存于本机应用私有目录，不上传、不联网。详见 [`docs/privacy.md`](docs/privacy.md)。
