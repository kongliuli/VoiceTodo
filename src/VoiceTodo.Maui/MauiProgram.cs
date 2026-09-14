using Plugin.LocalNotification;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Resources;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.PlatformServices;
using VoiceTodo.Maui.Services;
using VoiceTodo.Maui.ViewModels;

namespace VoiceTodo.Maui;

public static class MauiProgram
{
    /// <summary>把全局默认提醒配置（场景/设置页）映射为新建命令的 ReminderSettings 默认值。</summary>
    private static ReminderSettings BuildReminderSettings(ReminderDefaults d) => new()
    {
        NagMode = d.NagMode,
        Snooze = d.Snooze,
        PreAlert = d.PreAlert
    };

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
#if ANDROID
            // Plugin.LocalNotification 仅支持 iOS/Android。Windows 由 WindowsNotificationScheduler 走系统 toast。
            .UseLocalNotification()
#endif
            ;

        // 端侧模型提供方：从内嵌 manifest.json 读取全部候选模型，按“激活模型”解析本地目录
        // （首次从包内 MauiAsset 提取到 AppData/Models）。设置页可切换激活模型。
        builder.Services.AddSingleton<IModelProvider, ModelProvider>();

        // Core 服务（与平台无关）
        builder.Services.AddSingleton<ILocalizer, CoreLocalizer>();
        builder.Services.AddSingleton<ITimeParser>(sp =>
            new CompositeTimeParser(new ITimeParser[]
            {
                new ChineseTimeParser(),
                new EnglishTimeParser()
            }));
        builder.Services.AddSingleton<IIntentParser, RuleBasedIntentParser>();
        builder.Services.AddSingleton<ITodoRepository>(_ =>
            new JsonFileTodoRepository()); // 生产可换 SQLite 实现
        builder.Services.AddSingleton<VoiceTodo.Core.Abstractions.ITextToSpeech, NativeTextToSpeech>();
        builder.Services.AddSingleton<INotificationScheduler,
#if ANDROID
            LocalNotificationScheduler>()
#else
            WindowsNotificationScheduler>()
#endif
        ;
        // DEV-03 真实录音：按平台注册麦克风采集实现（DemoMicrophoneCapture 保留但不再默认注入）
        builder.Services.AddSingleton<IMicrophoneCapture,
#if ANDROID
            AndroidAudioMicrophoneCapture>()
#else
            WindowsAudioMicrophoneCapture>()
#endif
        ;
        // DEV-02 提醒闭环：仓库变更与通知调度的唯一入口（契约注册，实现由 Core 提供）
        builder.Services.AddSingleton<VoiceTodo.Core.Abstractions.ITodoChangeDispatcher, VoiceTodo.Core.Services.TodoChangeDispatcher>();
        // DEV-05 活动计时运行快照存储（running-timer.json，纯 Core，可单测）
        builder.Services.AddSingleton<RunningTimerStore>();
        // DEV-05 末秒倒数/阶段提示的单一播报队列（过期丢弃、暂停清空、恢复不补播）
        builder.Services.AddSingleton<AnnouncerQueue>();
        // DEV-05 计时前台服务：Android specialUse FGS 保活 + 常驻通知（暂停/停止按钮）；Windows 空实现
        builder.Services.AddSingleton<ITimerForegroundController,
#if ANDROID
            TimerForegroundController>()
#else
            NoopTimerForegroundController>()
#endif
        ;
        // B6 背景音乐共存：Android 用 MAY_DUCK 音频焦点，其它平台空实现。
        builder.Services.AddSingleton<IAudioCoexist,
#if ANDROID
            AndroidAudioCoexist>()
#else
            NoopAudioCoexist>()
#endif
        ;
        builder.Services.AddSingleton<ISpeechRecognizer, WhisperSpeechRecognizer>();
        builder.Services.AddSingleton(p => new VoicePipeline(
            p.GetRequiredService<IIntentParser>(),
            p.GetRequiredService<ITodoRepository>(),
            p.GetRequiredService<INotificationScheduler>(),
            p.GetRequiredService<VoiceTodo.Core.Abstractions.ITextToSpeech>(),
            p.GetRequiredService<ILocalizer>(),
            () => BuildReminderSettings(AppSettings.SceneDefaults),
            // L2 半截通路接线：语音建的计时落库即就绪 → 投放计时页；语音命令即完整有效指令，直接开跑
            item => TimerLauncher.RequestStart(item, autoStart: true)));

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();

        // C 组功能（feature-builder-b）：数据备份与自定义训练模板
        builder.Services.AddSingleton<TodoBackupService>();
        builder.Services.AddSingleton<TemplateStore>();

        return builder.Build();
    }
}
