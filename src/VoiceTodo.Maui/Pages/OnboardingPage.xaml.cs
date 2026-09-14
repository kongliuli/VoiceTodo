using Microsoft.Maui.Storage;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.Pages;

/// <summary>首次使用引导页：功能简介与入口选择，完成后写入 onboarding.done。</summary>
public partial class OnboardingPage : ContentPage
{
    private const string DoneKey = "onboarding.done";

    public OnboardingPage()
    {
        InitializeComponent();
        HeadLabel.Text = AppResources.OnbHead;
        SubLabel.Text = AppResources.OnbSub;
        QuoteLabel.Text = AppResources.OnbQuote;
        ResultLabel.Text = AppResources.OnbResult;
        F1Head.Text = AppResources.OnbF1Head;
        F1Sub.Text = AppResources.OnbF1Sub;
        F2Head.Text = AppResources.OnbF2Head;
        F2Sub.Text = AppResources.OnbF2Sub;
        FineLabel.Text = AppResources.OnbFine;
        StartBtn.Text = AppResources.GetStartedBtn;
        TextBtn.Text = AppResources.TextFirstBtn;
    }

    private async void OnGetStarted(object sender, EventArgs e)
    {
        Preferences.Set(DoneKey, true);
        // A1：引导完成即申请通知权限（用户可见时机，非冷启动弹窗）。被拒不阻断流程。
        await NotificationPermission.EnsureAsync();
        await Shell.Current.GoToAsync("//MainPage");
    }

    private async void OnTextFirst(object sender, EventArgs e)
    {
        Preferences.Set(DoneKey, true);
        // A1：引导完成即申请通知权限（用户可见时机，非冷启动弹窗）。被拒不阻断流程。
        await NotificationPermission.EnsureAsync();
        await Shell.Current.GoToAsync("//MainPage"); // 先回主 Tab，再推子页
        await Shell.Current.GoToAsync("TextAddPage");
    }
}
