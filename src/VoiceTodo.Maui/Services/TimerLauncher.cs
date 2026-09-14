using VoiceTodo.Core.Models;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 跨页启动计时请求：计时编辑器「保存并开始」投放，TimerPage OnAppearing 消费并自动开始。
/// </summary>
public static class TimerLauncher
{
    /// <summary>待自动开始的计时（消费后置空）。</summary>
    public static TimerItem? PendingStart { get; set; }

    /// <summary>
    /// 消费 PendingStart 时是否直接开跑：完整有效指令（口述/文字/「保存并开始」）为 true，
    /// 计时页随即进入运行态而非准备态（见 page-optimization-review §完整口述计划没有自动开始训练）。
    /// </summary>
    public static bool PendingAutoStart { get; set; }

    /// <summary>
    /// DEV-05「识别有误，停止并修改」：运行页停止后暂存的当前计划快照，
    /// 供计时编辑器（DEV-04）打开时预填。编辑器 OnAppearing 读取并消费此字段。
    /// </summary>
    public static TrainingPlan? PendingEditPlan { get; set; }

    public static void RequestStart(TimerItem item, bool autoStart = false)
    {
        PendingStart = item;
        PendingAutoStart = autoStart;
        Changed?.Invoke();
    }

    public static event Action? Changed;
}
