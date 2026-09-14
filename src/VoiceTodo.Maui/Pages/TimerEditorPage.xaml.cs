using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System.Threading;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 通用训练计划编辑器（08）：倒计时 / 计划双面板。计划面板字段 = 动作名 / 组数 / 每组运动 / 组间休息 /
/// 末组休息开关 / 倒数窗口（5s/3s/关）/ 阶段播报开关；保存写 TimerItem.Plan + Duration=计划总时长，
/// Phases 由计划展开。来源两类：模板编辑（默认值）与「停止后纠错 / 追问补齐」（查询参数预填），
/// 保存并开始仍经 TimerLauncher.RequestStart 投放 TimerPage。
/// </summary>
[QueryProperty(nameof(QName), "name")]
[QueryProperty(nameof(QRounds), "rounds")]
[QueryProperty(nameof(QWork), "work")]
[QueryProperty(nameof(QRest), "rest")]
[QueryProperty(nameof(QRestLast), "restlast")]
public partial class TimerEditorPage : ContentPage
{
    private const string TtsCuesKey = "ttsCues";

    private readonly ITodoRepository _repo;
    private readonly TemplateStore _templates;
    private readonly List<Border> _userChips = new(); // C4：动态用户模板 chip
    private CancellationTokenSource? _lpCts;          // C4：长按删除计时
    private bool _longPressFired;

    private int _segment = 1; // 0=倒计时 1=训练计划（默认计划）
    private string _tpl = "tabata"; // tabata / hiit / custom
    private int _workSec = 20;
    private int _restSec = 10;
    private int _rounds = 8;
    private int _cdSec = 300;
    private bool _restLast;      // 末组休息（默认关：深蹲例 10×5+9×10=02:20）
    private int _cdWindow;       // 阶段末倒数窗口（秒）：5 / 3 / 0=关
    private bool _prefilled;     // 查询参数预填只执行一次

    // 查询参数（停止后纠错 / 追问补齐入口）
    public string? QName { get; set; }
    public string? QRounds { get; set; }
    public string? QWork { get; set; }
    public string? QRest { get; set; }
    public string? QRestLast { get; set; }

    public TimerEditorPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _repo = services.GetRequiredService<ITodoRepository>();
        _templates = services.GetRequiredService<TemplateStore>();

        Title = AppResources.NewTimerTitle;
        TitleLabel.Text = AppResources.NewTimerTitle;
        BackBtn.Text = "←";
        SegCountdown.Text = AppResources.Countdown;
        SegInterval.Text = AppResources.IntervalTraining;

        // 动作名即计划标题（08：不再是固定 Tabata 专属表单）
        NameHeader.Text = AppResources.ActionNameLabel;
        NameEntry.Placeholder = AppResources.PlanActionPlaceholder;
        ClearBtn.Text = "⊗";

        TemplateHeaderLabel.Text = AppResources.TemplateHeader;
        TabataBtn.Text = "Tabata";
        HiitBtn.Text = "HIIT";
        CustomBtn.Text = AppResources.TplCustom;
        SaveTplBtn.SetValue(SemanticProperties.DescriptionProperty, AppResources.SaveAsTemplate);

        WorkRowLabel.Text = AppResources.WorkLabel;
        RestRowLabel.Text = AppResources.RestLabel;
        RoundsRowLabel.Text = AppResources.RoundsLabel;
        WorkMinus.Text = "−"; WorkPlus.Text = "+";
        RestMinus.Text = "−"; RestPlus.Text = "+";
        RoundsMinus.Text = "−"; RoundsPlus.Text = "+";

        RestLastLabel.Text = AppResources.RestLastLabel;
        CountdownWindowLabel.Text = AppResources.CountdownWindowLabel;
        Win5Btn.Text = AppResources.PlanWindow5;
        Win3Btn.Text = AppResources.PlanWindow3;
        WinOffBtn.Text = AppResources.PlanWindowOff;

        PreviewHeaderLabel.Text = AppResources.PreviewHeader;

        CdMinus.Text = "−"; CdPlus.Text = "+";
        Quick5.Text = string.Format(AppResources.QuickMinFormat, 5);
        Quick10.Text = string.Format(AppResources.QuickMinFormat, 10);
        Quick25.Text = string.Format(AppResources.QuickMinFormat, 25);

        // 阶段播报（「第1组，深蹲」「休息10秒」）；同时写全局 ttsCues 偏好供运行页读取
        TtsLabel.Text = AppResources.PhaseAnnounceLabel;
        TtsSub.Text = AppResources.PhaseAnnounceSub;
        TtsSwitch.IsToggled = Preferences.Get(TtsCuesKey, true);

        SaveStartBtn.Text = AppResources.SaveStartBtn;
        SaveOnlyBtn.Text = AppResources.SaveOnlyBtn;

        RefreshSeg();
        RefreshTplChips();
        RefreshValues();
        RefreshWindowChips();
        RenderPreview();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_prefilled) return;
        _prefilled = true;
        ApplyQueryPrefill();
        ApplyPendingEditPlan();
    }

    /// <summary>
    /// 消费「停止并修改」暂存的计划快照（TimerLauncher.PendingEditPlan）：预填计划字段，
    /// 与查询参数入口互斥（带查询参数时以查询参数为准，避免两套来源打架）。
    /// </summary>
    private void ApplyPendingEditPlan()
    {
        var plan = TimerLauncher.PendingEditPlan;
        if (plan is null) return;
        TimerLauncher.PendingEditPlan = null; // 一次性消费
        if (QName is not null || QRounds is not null || QWork is not null || QRest is not null || QRestLast is not null)
            return; // 查询参数入口优先

        NameEntry.Text = plan.ActionName;
        _segment = 1;
        _tpl = "custom";
        _rounds = Math.Clamp(plan.Rounds, 1, 30);
        _workSec = Math.Max(5, (int)plan.WorkDuration.TotalSeconds);
        _restSec = Math.Max(0, (int)plan.RestDuration.TotalSeconds);
        _restLast = plan.RestAfterLastRound;
        _cdWindow = plan.CountdownWindowSeconds is 5 or 3 ? plan.CountdownWindowSeconds : 0;
        RestLastSwitch.IsToggled = _restLast;
        TtsSwitch.IsToggled = plan.PhaseAnnouncements;

        RefreshSeg();
        RefreshTplChips();
        RefreshValues();
        RefreshWindowChips();
        RenderPreview();

        Title = AppResources.EditPlanTitle;
        TitleLabel.Text = AppResources.EditPlanTitle;
    }

    /// <summary>查询参数预填（纠错/补齐入口）：带参数即视为编辑来源，标题切换并应用计划字段。</summary>
    private void ApplyQueryPrefill()
    {
        if (QName is null && QRounds is null && QWork is null && QRest is null && QRestLast is null) return;

        if (QName is not null) NameEntry.Text = Uri.UnescapeDataString(QName);
        if (int.TryParse(QRounds, out var r) && r > 0) _rounds = Math.Clamp(r, 1, 99);
        if (int.TryParse(QWork, out var w) && w > 0) _workSec = w;
        if (int.TryParse(QRest, out var rest) && rest >= 0) _restSec = rest;
        _restLast = QRestLast == "1";
        RestLastSwitch.IsToggled = _restLast;
        _segment = 1;
        _tpl = "custom";
        RefreshSeg();
        RefreshTplChips();
        RefreshValues();
        RenderPreview();

        Title = AppResources.EditPlanTitle;
        TitleLabel.Text = AppResources.EditPlanTitle;
    }

    private static Color ResColor(string key) => (Color)Application.Current!.Resources[key];

    private static string Fmt(int sec) => TimeSpan.FromSeconds(sec).ToString(@"mm\:ss");

    // ══════════ 分段与面板 ══════════

    private void OnSegment(object sender, EventArgs e)
    {
        _segment = sender == SegInterval ? 1 : 0;
        RefreshSeg();
    }

    /// <summary>分段选中态：选中 bg=Surface、text=TextPrimary；未选透明、text=TextSecondary。</summary>
    private void RefreshSeg()
    {
        bool interval = _segment == 1;
        SegCountdown.BackgroundColor = interval ? Colors.Transparent : ResColor("Surface");
        SegCountdown.TextColor = interval ? ResColor("TextSecondary") : ResColor("TextPrimary");
        SegInterval.BackgroundColor = interval ? ResColor("Surface") : Colors.Transparent;
        SegInterval.TextColor = interval ? ResColor("TextPrimary") : ResColor("TextSecondary");
        IntervalPanel.IsVisible = interval;
        CountdownPanel.IsVisible = !interval;
    }

    // ══════════ 模板 chips ══════════

    private void OnTpl(object? sender, EventArgs e)
    {
        if (_longPressFired) { _longPressFired = false; return; } // 长按删除已触发，忽略本次 tap
        if (sender is not Button btn) return;
        _tpl = btn.CommandParameter?.ToString() ?? "custom";
        if (_tpl == "tabata") { _workSec = 20; _restSec = 10; _rounds = 8; }
        else if (_tpl == "hiit") { _workSec = 30; _restSec = 15; _rounds = 6; }
        // custom：保持当前值不重置
        else if (_tpl != "custom")
        {
            // C4：用户自定义模板 → 套用其保存的参数
            var tpl = _templates.Load().FirstOrDefault(t => t.Name == _tpl);
            if (tpl is not null)
            {
                _rounds = tpl.Rounds;
                _workSec = tpl.WorkSec;
                _restSec = tpl.RestSec;
                _restLast = tpl.RestLast;
                _cdWindow = tpl.CdWindow;
                RestLastSwitch.IsToggled = _restLast;
            }
        }
        RefreshTplChips();
        RefreshValues();
        RenderPreview();
    }

    /// <summary>模板 chip 选中态：SelectedBg 底 + Primary 文字/描边；未选 Surface 底 + BorderColor 描边。</summary>
    private void RefreshTplChips()
    {
        void Apply(Border chip, Button btn, string key)
        {
            bool sel = _tpl == key;
            chip.BackgroundColor = sel ? ResColor("SelectedBg") : ResColor("Surface");
            chip.Stroke = sel ? ResColor("Primary") : ResColor("BorderColor");
            chip.StrokeThickness = sel ? 2 : 1;
            btn.TextColor = sel ? ResColor("Primary") : ResColor("TextSecondary");
        }
        Apply(TabataChip, TabataBtn, "tabata");
        Apply(HiitChip, HiitBtn, "hiit");
        Apply(CustomChip, CustomBtn, "custom");

        // C4：清掉上一批动态用户 chip，按当前已存模板重建
        foreach (var c in _userChips) TemplateChips.Children.Remove(c);
        _userChips.Clear();
        foreach (var ut in _templates.Load())
        {
            var chip = new Border { Style = (Style)Application.Current!.Resources["FilterChip"] };
            var btn = new Button
            {
                Style = (Style)Application.Current.Resources["GhostButton"],
                Padding = new Thickness(4, 4),
                Text = ut.Name,
                CommandParameter = ut.Name,
            };
            btn.Clicked += OnTpl;
            // 长按删除（带确认）：MAUI 无 LongPressGestureRecognizer，用 Pressed 起算 + Released 取消模拟
            btn.Pressed += (_, _) => BeginLongPress(ut.Name);
            btn.Released += (_, _) => _lpCts?.Cancel();
            chip.Content = btn;
            Apply(chip, btn, ut.Name);
            TemplateChips.Children.Add(chip);
            _userChips.Add(chip);
        }
    }

    // ══════════ C4 保存 / 复用 / 删除用户模板 ══════════

    /// <summary>「＋ 存为模板」：取名字 → 用当前参数保存（同名覆盖，达上限拒绝）→ 刷新 chip 行。</summary>
    private async void OnSaveTemplate(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppResources.SaveAsTemplateTitle, AppResources.TplNamePrompt,
            AppResources.OK, AppResources.Cancel);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        var tpl = new TrainingTemplate
        {
            Rounds = _rounds,
            WorkSec = _workSec,
            RestSec = _restSec,
            RestLast = _restLast,
            CdWindow = _cdWindow,
        };
        if (!_templates.Save(name, tpl))
        {
            await DisplayAlertAsync(AppResources.SaveAsTemplateTitle, AppResources.TplLimitReached, AppResources.OK);
            return;
        }
        AppLog.Info($"保存训练模板: {name}");
        await DisplayAlertAsync(AppResources.SaveAsTemplateTitle,
            string.Format(AppResources.TplSavedFormat, name), AppResources.OK);
        RefreshTplChips();
    }

    /// <summary>长按触发：起算 650ms，期间 Release 取消；到点则弹确认删除。</summary>
    private void BeginLongPress(string name)
    {
        _lpCts?.Cancel();
        _lpCts = new CancellationTokenSource();
        var cts = _lpCts;
        _ = LongPressDeleteAsync(name, cts);
    }

    private async Task LongPressDeleteAsync(string name, CancellationTokenSource cts)
    {
        try { await Task.Delay(650, cts.Token); }
        catch (TaskCanceledException) { return; }
        _longPressFired = true;
        await ConfirmDeleteTemplateAsync(name);
    }

    /// <summary>长按用户模板 chip：确认后删除并刷新（若当前选中的正是它，回退到 custom）。</summary>
    private async Task ConfirmDeleteTemplateAsync(string name)
    {
        var confirm = await DisplayAlertAsync(AppResources.TplDeleteTitle,
            string.Format(AppResources.TplDeleteConfirm, name), AppResources.OK, AppResources.Cancel);
        if (!confirm) return;
        _templates.Delete(name);
        if (_tpl == name) _tpl = "custom";
        AppLog.Info($"删除训练模板: {name}");
        RefreshTplChips();
        RefreshValues();
    }

    // ══════════ 参数步进 ══════════

    private void OnWorkMinus(object sender, EventArgs e) => StepWork(-5);
    private void OnWorkPlus(object sender, EventArgs e) => StepWork(5);
    private void OnRestMinus(object sender, EventArgs e) => StepRest(-5);
    private void OnRestPlus(object sender, EventArgs e) => StepRest(5);
    private void OnRoundsMinus(object sender, EventArgs e) => StepRounds(-1);
    private void OnRoundsPlus(object sender, EventArgs e) => StepRounds(1);
    private void OnCdMinus(object sender, EventArgs e) => StepCd(-30);
    private void OnCdPlus(object sender, EventArgs e) => StepCd(30);

    /// <summary>手动步进后若当前模板非 Custom 自动切为 Custom 态（仅样式高亮）。</summary>
    private void MarkCustom()
    {
        if (_tpl != "custom")
        {
            _tpl = "custom";
            RefreshTplChips();
        }
    }

    private void StepWork(int delta)
    {
        _workSec = Math.Max(5, _workSec + delta);
        MarkCustom();
        RefreshValues();
        RenderPreview();
    }

    private void StepRest(int delta)
    {
        _restSec = Math.Max(0, _restSec + delta); // 允许 0 = 组间无休息
        MarkCustom();
        RefreshValues();
        RenderPreview();
    }

    private void StepRounds(int delta)
    {
        _rounds = Math.Clamp(_rounds + delta, 1, 30);
        MarkCustom();
        RefreshValues();
        RenderPreview();
    }

    private void StepCd(int delta)
    {
        _cdSec = Math.Max(30, _cdSec + delta);
        RefreshValues();
    }

    private void OnQuickDur(object sender, EventArgs e)
    {
        if (sender is Button b && int.TryParse(b.CommandParameter?.ToString(), out int min) && min > 0)
        {
            _cdSec = min * 60;
            RefreshValues();
        }
    }

    // ══════════ 末组休息与倒数窗（DEV-04） ══════════

    private void OnRestLastToggled(object sender, ToggledEventArgs e)
    {
        _restLast = e.Value;
        RefreshValues();
        RenderPreview();
    }

    private void OnWindow(object sender, EventArgs e)
    {
        if (sender is Button b && int.TryParse(b.CommandParameter?.ToString(), out int sec))
        {
            _cdWindow = sec is 5 or 3 ? sec : 0;
            RefreshWindowChips();
        }
    }

    /// <summary>倒数窗 chip 选中态（同模板 chip 配色）。</summary>
    private void RefreshWindowChips()
    {
        void Apply(Border chip, Button btn, int value)
        {
            bool sel = _cdWindow == value;
            chip.BackgroundColor = sel ? ResColor("SelectedBg") : ResColor("Surface");
            chip.Stroke = sel ? ResColor("Primary") : ResColor("BorderColor");
            chip.StrokeThickness = sel ? 2 : 1;
            btn.TextColor = sel ? ResColor("Primary") : ResColor("TextSecondary");
        }
        Apply(Win5Chip, Win5Btn, 5);
        Apply(Win3Chip, Win3Btn, 3);
        Apply(WinOffChip, WinOffBtn, 0);
    }

    /// <summary>刷新步进器数值 + 图例 + 总时长（含末组休息口径）。</summary>
    private void RefreshValues()
    {
        WorkValue.Text = Fmt(_workSec);
        RestValue.Text = Fmt(_restSec);
        RoundsValue.Text = _rounds.ToString();
        CdValue.Text = Fmt(_cdSec);

        WorkLegendLabel.Text = string.Format(AppResources.WorkLegend, _workSec);
        RestLegendLabel.Text = string.Format(AppResources.RestLegend, _restSec);
        int total = PlanTotalSeconds();
        TotalLabel.Text = string.Format(AppResources.TotalFmt, Fmt(total));
    }

    /// <summary>计划总时长 = 组数×运动 +（组数-1+末组休息?1:0）×休息。</summary>
    private int PlanTotalSeconds() =>
        _rounds * _workSec + (_rounds - 1 + (_restLast ? 1 : 0)) * _restSec;

    // ══════════ 训练预览 ══════════

    /// <summary>重建预览色块：每轮 = 28 宽 Primary 实心块 + 12 宽 SelectedBg 浅蓝块，块下标轮号；
    /// 末组休息关闭时最后一轮省略休息块；超 12 轮截断加 "…"。</summary>
    private void RenderPreview()
    {
        PreviewBlocks.Children.Clear();
        int show = Math.Min(_rounds, 12);
        for (int i = 1; i <= show; i++)
        {
            var row = new HorizontalStackLayout { Spacing = 2 };
            row.Children.Add(new Border
            {
                WidthRequest = 28,
                HeightRequest = 20,
                BackgroundColor = ResColor("Primary"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }
            });
            bool showRest = i < _rounds || _restLast;
            if (showRest)
            {
                row.Children.Add(new Border
                {
                    WidthRequest = 12,
                    HeightRequest = 20,
                    BackgroundColor = ResColor("SelectedBg"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) }
                });
            }
            var col = new VerticalStackLayout { Spacing = 2 };
            col.Children.Add(row);
            col.Children.Add(new Label
            {
                Text = i.ToString(),
                FontSize = 10,
                TextColor = ResColor("TextMuted"),
                HorizontalOptions = LayoutOptions.Center
            });
            PreviewBlocks.Children.Add(col);
        }
        if (_rounds > 12)
        {
            PreviewBlocks.Children.Add(new Label
            {
                Text = "…",
                FontSize = 14,
                TextColor = ResColor("TextMuted"),
                VerticalOptions = LayoutOptions.Center
            });
        }
    }

    // ══════════ 其它交互 ══════════

    private void OnClearName(object sender, EventArgs e) => NameEntry.Text = string.Empty;

    private void OnTtsToggled(object sender, ToggledEventArgs e)
        => Preferences.Set(TtsCuesKey, e.Value);

    private async void OnBack(object sender, EventArgs e) => await GoBackAsync();

    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    // ══════════ 保存 ══════════

    private TimerItem BuildItem()
    {
        bool interval = _segment == 1;
        string title = string.IsNullOrWhiteSpace(NameEntry.Text)
            ? (interval ? AppResources.IntervalTraining : AppResources.Countdown)
            : NameEntry.Text.Trim();
        if (!interval)
        {
            return new TimerItem
            {
                Title = title,
                Duration = TimeSpan.FromSeconds(_cdSec),
                TriggerAt = DateTimeOffset.Now,
                Phases = null,
                Reminder = new()
            };
        }

        // DEV-04：保存写统一计划快照（Duration=总时长、Phases 由计划展开）
        var plan = new TrainingPlan
        {
            ActionName = title,
            Rounds = _rounds,
            WorkDuration = TimeSpan.FromSeconds(_workSec),
            RestDuration = TimeSpan.FromSeconds(_restSec),
            RestAfterLastRound = _restLast,
            CountdownWindowSeconds = _cdWindow,
            PhaseAnnouncements = TtsSwitch.IsToggled
        };
        return new TimerItem
        {
            Title = plan.ActionName,
            Duration = plan.TotalDuration,
            TriggerAt = DateTimeOffset.Now,
            Phases = plan.Expand(),
            Plan = plan,
            Reminder = new()
        };
    }

    private async void OnSaveStart(object sender, EventArgs e)
    {
        var item = BuildItem();
        try
        {
            await _repo.AddTimerAsync(item);
        }
        catch (StorageException ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex.Message); // 保存失败：留在编辑器，不丢输入
            return;
        }
        TimerLauncher.RequestStart(item, autoStart: true); // 「保存并开始」= 完整有效指令，直接开跑
        await Shell.Current.GoToAsync("//TimerPage");
    }

    private async void OnSaveOnly(object sender, EventArgs e)
    {
        try
        {
            await _repo.AddTimerAsync(BuildItem());
        }
        catch (StorageException ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
            return;
        }
        await GoBackAsync();
    }
}
