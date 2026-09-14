# 端侧模型：多模型并存与切换

VoiceTodo 的 ASR / TTS 全部走 sherpa-onnx（端侧离线）。本方案支持**把所有候选模型都下载并存保留**，然后在应用里**运行时切换**激活模型，逐个测效果；并通过**打包清单**控制每次构建把哪些模型打进应用包。

## 1. 模型目录（manifest.json）

唯一事实源：`src/VoiceTodo.Maui/Resources/Models/manifest.json`。

每个条目字段：

| 字段 | 含义 |
|---|---|
| `Id` | 唯一 ID，用于激活与打包引用 |
| `Name` | 设置页展示名 |
| `Kind` | `Asr` / `Tts` / `Nlu` |
| `Culture` | 语种；`null` = 多语/双语（如 SenseVoice、Melo） |
| `Engine` | sherpa 引擎：`sense-voice` / `whisper` / `paraformer` / `moonshine` / `vits` / `kokoro` |
| `RelativePath` | 相对 `Models/` 的路径，如 `asr/sensevoice-int8` |
| `Files` | 顶层文件清单（就绪校验用） |
| `ArchiveUrl` | 发布包 tarball 地址（下载脚本下载+解包） |
| `DownloadBaseUrl` | 逐文件下载基址（无 ArchiveUrl 时） |
| `IsDefault` | 同类默认激活项 |
| `PackInBuild` | 是否随包分发 |
| `Options` | 引擎附加项，如 `ModelFile` 覆盖、Kokoro 的 `Voices` 等 |

当前预置候选（详见 manifest.json）：

- **ASR**：SenseVoice-int8（中英日韩粤，默认）、Whisper-tiny、Paraformer-zh、Moonshine-base-en
- **TTS**：VITS-Melo（中英双语，默认）、Piper-en、Kokoro-multi（中英多语，高质量）

> 注：部分 `ArchiveUrl`（Moonshine / Piper 的具体发布文件名）为按 sherpa-onnx 命名惯例填写，若下载脚本报 404，请到 https://github.com/k2-fsa/sherpa-onnx/releases 核对准确文件名后更新 manifest.json 即可，不影响其它模型。

## 2. 全部下载（并存）

```powershell
pwsh scripts/download-models.ps1
```

- 读取 manifest.json，将**每个**条目下载解包到仓库根的 `ModelsLibrary/<RelativePath>/`（所有模型并存，不进 git）。
- 每个模型目录自动生成 `.filelist`（递归文件清单，含 `espeak-ng-data/` 等子目录），供打包与运行时提取。
- 单个失败只告警并继续。

## 3. 切换打包（控制包体积）

打包清单 `models.pack.json` 决定每次构建把哪些模型打进 `Resources/Models/`：

```json
{ "pack": [ "asr-sensevoice-int8", "tts-melo-zh-en" ] }
```

```powershell
pwsh scripts/pack-models.ps1            # 按 models.pack.json 打包
pwsh scripts/pack-models.ps1 -All       # 打包 ModelsLibrary 中的全部模型
```

- 脚本把 `ModelsLibrary/<relpath>` 复制到 `src/VoiceTodo.Maui/Resources/Models/<relpath>`（含 `.filelist` 与子目录）。
- 未列入的模型会从 `Resources/Models` 移除，从而不进包。
- 改完清单后重新构建 MAUI 应用即生效。

## 4. 运行时切换（逐个测试）

设置页（Settings）新增 **ASR 模型** 与 **TTS 模型** 两个下拉框：

- 列出同类全部候选；未随包的项标注「（未随包，需切换打包）」。
- 选择即写入 `Preferences`（`model.active.Asr` / `model.active.Tts`）。
- 下次识别/合成时，`SherpaOnnxSpeechRecognizer` / `SherpaOnnxTextToSpeech` 检测到激活 ID 变化，自动重建对应引擎，**无需重启应用**。

切换逻辑由 `IModelProvider` 提供：`GetModels(kind)` / `GetActive(kind)` / `SetActive(kind, id)` / `ResolveActiveAsync(kind)`。

## 5. 数据流

```
manifest.json (候选目录)
   └─ download-models.ps1 ──> ModelsLibrary/<relpath>/  (全部并存, .gitignore)
          └─ pack-models.ps1 ──> Resources/Models/<relpath>/  (本次打包的子集, MauiAsset)
                 └─ 运行时 ──> AppData/Models/<relpath>/  (首次从包内提取, 供 sherpa-onnx 读取)
```
