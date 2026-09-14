using VoiceTodo.Maui.Pages;

namespace VoiceTodo.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // 子页路由：清单/计时为主导航 Tab；其余页面自各入口 push 进入。
        Routing.RegisterRoute("SettingsPage", typeof(SettingsPage));
        Routing.RegisterRoute("CalendarPage", typeof(CalendarPage));
        Routing.RegisterRoute("TaskEditorPage", typeof(Pages.TaskEditorPage));
        Routing.RegisterRoute("TimerEditorPage", typeof(Pages.TimerEditorPage));
        Routing.RegisterRoute("VoiceCapturePage", typeof(Pages.VoiceCapturePage));
        Routing.RegisterRoute("TextAddPage", typeof(Pages.TextAddPage));
        Routing.RegisterRoute("ReminderPage", typeof(Pages.ReminderPage));
        Routing.RegisterRoute("OnboardingPage", typeof(Pages.OnboardingPage));
    }

    private bool _navChecked;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_navChecked) return;
        _navChecked = true;
        // 首次使用 → 引导页（13 设计稿）
        if (!Microsoft.Maui.Storage.Preferences.Default.Get("onboarding.done", false))
            await Shell.Current.GoToAsync("OnboardingPage");
    }
}
