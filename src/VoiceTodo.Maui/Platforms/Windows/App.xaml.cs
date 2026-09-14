using Microsoft.Maui;
using Microsoft.UI.Xaml;

namespace VoiceTodo.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "firstchance.log"),
                    $"{DateTime.Now:O} {e.Exception.GetType().Name}: {e.Exception.Message}{Environment.NewLine}" +
                    (e.Exception.StackTrace ?? "").Split('\n')[0] + Environment.NewLine);
            }
            catch { }
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
