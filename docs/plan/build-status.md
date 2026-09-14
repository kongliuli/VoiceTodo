# 构建现状与已完成内容（Build Status）

> 记录时间：2026-09-10
> 目标平台：`.NET 10 / .NET MAUI 10`（net10.0、net10.0-windows10.0.19041、net10.0-android）
> 目的：让 `VoiceTodo.Maui` 能跑通构建，以便实测 ASR/TTS 模型。

## 0. 一句话结论

`win10` MAUI 工作负载已确认就绪，构建环境不是问题。当前真正卡住构建的有**两类**：

1. **Windows 目标**被 `.NET 10 SDK + 当前 WindowsAppSDK` 的 WinUI XAML 编译器 bug 卡死（WMC0001，无法靠改包版本修掉）。
2. **所有目标**（含 Android）被 `PlatformServices` 下的代码与所引用 NuGet 包 **API 版本不匹配**卡死（sherpa-onnx、LocalNotification、RESX 生成共约 18+ 处错误）。

环境之外，**已修掉 3 个真实的代码 bug**（见第 2 节）。

---

## 1. 已验证的环境事实

| 项 | 结论 |
|---|---|
| win10 MAUI 工作负载 | ✅ 已安装 `net10.0-windows10.0.19041`，`maui`/`windows` workload 均在 |
| 网络 | ✅ 可访问 `api.nuget.org`，可联网拉包 |
| `MauiWinUIApplication` 类型所在 | 真实类型，落在 `Microsoft.Maui.dll`（10.0.20 windows 版），**无 ref 程序集干扰** |
| XAML `clr-namespace:Microsoft.Maui;assembly=Microsoft.Maui` | ✅ 写法正确（类型、引用、命名空间都没错） |
| `Plugin.LocalNotification` 兼容版本 | `13.0.0` 支持 net10 且不依赖 WindowsAppSDK 2.4（14.x 才跳到 2.4，与 MAUI 10 冲突） |

---

## 2. 已完成的修复（真实代码 bug）

| # | 问题 | 文件 | 修复 |
|---|---|---|---|
| 1 | Windows `App.xaml` 缺 `xmlns:x`，导致 WMC9999 | `Platforms/Windows/App.xaml` | 补 `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"` |
| 2 | `ITextToSpeech` 与 MAUI 10 自带的 `Microsoft.Maui.Media.ITextToSpeech` 重名歧义 | `PlatformServices/SherpaOnnxTextToSpeech.cs` | 显式限定为 `VoiceTodo.Core.Abstractions.ITextToSpeech` |
| 3 | `using LocalNotification;` 命名空间写错（真实命名空间是 `Plugin.LocalNotification`） | `MauiProgram.cs`、`PlatformServices/LocalNotificationScheduler.cs` | 改为 `using Plugin.LocalNotification;` |

> 第 3 项是通过临时 C# 程序用 `System.Reflection.Metadata` 读取 dll 确认的：`LocalNotificationCenter` / `NotificationRequest` 等确在 `Plugin.LocalNotification` 命名空间下，11.1.2 与 13.0.0 均如此。原 `using LocalNotification;` 从未对得上这两版。

### 已调整的包版本（联网更新）

- `Plugin.LocalNotification`：`11.1.2` → `13.0.0`
- `Plugin.Maui.Audio`：`3.0.0` → `4.0.0`
- `Microsoft.WindowsAppSDK`：钉定 `1.7.251014001`，并加 `Condition` 限定为 **仅 Windows 目标**（避免它在 Android TFM 下因「需要 Windows TFM」报错）

---

## 3. 剩余阻塞（待解决）

### 阻塞 A — Windows 目标：WMC0001（工具链 bug，非代码）

`Platforms/Windows/App.xaml` 中 `MauiWinUIApplication` 类型解析失败，已逐项验证：

- 类型确实存在、`input.json` 引用正确、命名空间正确；
- 升到稳定版 `WindowsAppSDK 1.7.251014001` 依旧失败 → **排除版本因素**；
- `dotnet clean` + Rebuild 重生成 `input.json` 后依旧失败 → **排除缓存**；
- 手动跑外部 `XamlCompiler.exe` 能成功，但**当前 WindowsAppSDK 已禁用外部编译器**（`UseXamlCompilerExecutable` 被强制 false，并报 “no longer supported”），只剩进程内编译器，而它解析不到该类型。

**定性**：这是 `.NET 10 SDK + 当前 WinAppSDK` 的 WinUI XAML 编译器不兼容，在本环境内无法靠改包版本修复。

### 阻塞 B — 所有目标：代码与 NuGet 包 API 不匹配（代码级）

修好命名空间后，编译暴露出 `PlatformServices` 下代码针对的是**与当前包版本不同的 API**：

- **sherpa-onnx（1.11.2）**：`SherpaOnnxSpeechRecognizer.cs` / `SherpaOnnxTextToSpeech.cs` 用到已不存在/改名的 API：
  - `OfflineRecognizerConfig.Model`、`OfflineModelConfig.SampleRate`、`OfflineMoonshineModelConfig.Model`
  - `OfflineTtsKokoroModelConfig.Lang`、`OfflineTtsVitsModelConfig.Lang`
  - `OfflineTts.Generate(...)` 多出一个 `speakerId` 参数
  - `OfflineRecognizer.GetResult()`、`WaveReader` 已不存在
- **LocalNotification（13.0.0）**：`LocalNotificationScheduler.cs` 用到已变动的 API：
  - `NotificationSound` 找不到（可能改名/移除）
  - `DateTimeOffset` → `DateTime?` 隐式转换失败
  - 某 `bool` 结果调用 `.GetAwaiter()`（方法签名由 `Task` 变 `bool`）
- **RESX 生成**：`App.xaml.cs:15` 报 `AppResources` 不存在（生成类/命名空间不匹配）

> 说明：这些文件（sherpa / LocalNotification 封装）明显是按**某个非当前版本**写的，需对齐后才能编译。

---

## 4. 待用户决策

1. **SherpaOnnx / LocalNotification / AppResources 代码**
   - **(A)** 按当前钉定的包版本（`sherpa-onnx 1.11.2`、`Plugin.LocalNotification 13.0.0`）**重写** `PlatformServices` 相关文件，保留多模型/多引擎逻辑；
   - **(B)** 反过来把包版本**降到与现有代码匹配**的版本（需先查清代码原本对应的版本）。

2. **Windows 目标**
   - 当前被工具链 bug 卡死。可选：
     - 暂时放弃 Windows，先跑通 **Android**（需先解决上面的 API 不匹配）；
     - 或将工程整体降到 **MAUI 8 / net8.0**（本机缓存有稳定 `WindowsAppSDK 1.6`，WinUI XAML 编译器可正常工作）。

---

## 5. 备注 / 临时产物

- 调试期间在 `src/VoiceTodo.Maui/` 下产生过 `build.log`、`*.orig` 等临时文件，需要时清理（删除命令此前因审批超时未执行）。
- 排查 `Plugin.LocalNotification` 命名空间时，曾于 `C:\temp\nscheck` 建临时校验工程，可删除。
