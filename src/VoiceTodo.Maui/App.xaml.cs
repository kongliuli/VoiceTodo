using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        // 启动即回读持久化语言与设置（i18n 从启动开始；不再等打开设置页才生效）
        AppSettings.Load();
        // A5：启动时 fire-and-forget 清理 7 天前的录音（不 await，绝不阻塞 UI 线程；清理失败静默忽略）
        _ = Task.Run(RecordingJanitor.CleanupOld);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
