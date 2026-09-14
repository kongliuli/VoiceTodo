# 开源模型与项目可行性调研 — 支撑 A/B/C/D 的端侧方案

> 调研时间：2026-09-10
> 目标：确认 A/B/C/D 各功能是否有成熟开源项目在 .NET MAUI 上**端侧、离线**支撑。

## 总览结论

**A/B/C/D 四类功能均可由成熟开源项目端侧离线支撑，无需云端。** 唯一"重"点是端侧小模型（SLM）做高级意图理解，建议作为可选增强而非 MVP 必备。

## 1. 语音识别 STT（"听"）

| 项目 | 说明 | MAUI 移动端 |
|---|---|---|
| **sherpa-onnx**（`org.k2fsa.sherpa.onnx` NuGet） | Next-gen Kaldi，一套包同时支持 **ASR+TTS+说话人识别**；明确支持 Android/iOS/Win/Linux/嵌入式；C# API 完整 | ✅ 移动端一等公民，最省心 |
| **whisper.net**（`Whisper.net` NuGet） | 封装 whisper.cpp；确认支持 Windows/Linux/**Android/Apple 移动端**（CoreML 静态库，已简化上架） | ✅ 需按平台配 Runtime 包 |

**轻量模型（经 sherpa-onnx 跑手机）**：
- **Moonshine**（比 Whisper 更小更快，适合实时）
- **SenseVoice**（阿里，多语种 + 快，含情绪/事件检测）
- **Paraformer**（中文强）

**选型**：STT+TTS 二合一选 **sherpa-onnx**；中文为主可优先 SenseVoice/Paraformer 模型。

## 2. 语音合成 TTS（"说"）

- **sherpa-onnx TTS**：支持 **Piper / VITS / Kokoro-82M**，100% 离线。Piper 有中文嗓音，Kokoro 质量高。
- 支撑 **B 的语音阶段播报**（"Work/Rest/倒数3-2-1"）与 **A 的"用自己声音播报提醒"**。
- 备选：平台原生 TTS（Android `TextToSpeech` / iOS `AVSpeechSynthesizer`），更简单但可控性差。

## 3. 意图识别 / NLU（"懂"）

- **规则方案（推荐 MVP）**：正则/关键词匹配（举杠铃→计时器，提醒/催促→提醒，待办/买/做→任务）。零模型、纯离线、稳定。
- **端侧 SLM（可选增强）**：Phi-3-mini 经 ONNX Runtime GenAI（`Microsoft.ML.OnnxRuntimeGenAI`）。**注意**：GenAI 移动端（Android/iOS）支持目前以 Windows 示例为主，尚不成熟；手机上跑 2B 级模型可考虑 LLamaSharp（llama.cpp 绑定），但偏重。
- **结论**：MVP 用规则 NLU，SLM 留作后续增强。

## 4. 自然语言时间解析（C 的核心）

- **nChronic / ChronicNetCore**：.NET 移植版 Ruby Chronic，但偏**英文**。
- **中文专用**：`cn2t`（CN2T 中文时间解析器）、`zh-time-parser`(2026)、JioNLP TimeParser、Time-NLP——多为 **Python**。
- **落地建议**：自写覆盖常见中文模式的解析器（分钟/小时/天/周/后天/每天周一/5分钟后），规则+正则即可离线搞定；若需更强泛化，再移植 cn2t 或上小模型。

## 5. 提醒/定时器触发（A 的 Nag Mode、B 的定时器）

- **Plugin.LocalNotification**（MAUI 跨平台 Android/iOS/Mac/Win）：支持**定时通知、自定义铃声、通知按钮（完成/推迟/忽略）、周期重复** → 直接支撑 **Nag Mode（重复直到确认）+ Snooze（改期）+ Pre-alarm（预提醒）**。
- 自定义语音铃声：录一段（MAUI 录音）→ 或 sherpa-onnx TTS 生成语音文件 → 设为通知音。

## 6. 本地持久化（D 的离线）

- **SQLite**（`sqlite-net-pcl` 或 EF Core）：待办、定时器、模板全本地存。

## 7. 逐项映射表

| 功能分组 | 开源支撑 | 端侧可行 |
|---|---|---|
| A 提醒可靠性（Nag/Snooze/语音播报） | Plugin.LocalNotification + sherpa-onnx TTS | ✅ |
| B 计时灵活度（可变间歇/语音引导/预设） | 应用逻辑 + sherpa-onnx TTS + AudioFocus | ✅ |
| C 语义自然度（相对时间/多语） | 中文时间解析器 + sherpa-onnx 多语模型 | ✅ |
| D 隐私本地化 | whisper.net/sherpa-onnx + 规则 NLU + SQLite | ✅ |

**模型体积参考（手机可装）**：Whisper tiny ~75MB / SenseVoice-small 数十 MB / Piper-Kokoro TTS <100MB，CPU+GPU 加速可达近实时。

## 8. 最终选型定案

- 框架：.NET 9/10 + .NET MAUI。
- 语音：**sherpa-onnx**（STT+TTS 一体，优先 SenseVoice/Paraformer 中文模型；备选 whisper.net）。
- 意图/时间：**规则 NLU** + 自写**中文时间解析器**（英文走 nChronic）。
- 提醒/定时器：**Plugin.LocalNotification**。
- 存储：**SQLite**。
- 多语言：.NET RESX + CultureInfo 提供器，中/英起步，TTS 按 locale 选嗓音。
