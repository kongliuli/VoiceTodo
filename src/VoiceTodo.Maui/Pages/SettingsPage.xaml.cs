using System.Globalization;
using System.ComponentModel;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.DependencyInjection;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Resources;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly IModelProvider _provider = null!;
    private readonly TodoBackupService _backup;
    private readonly ITodoRepository _repo;
    private List<ModelEntry> _asrModels = new();
    private bool _initializing = true;
    private PermissionStatus? _micStatus;
    private PermissionStatus? _notifStatus;

    // 免提场景：显示名 -> Scene 值
    private static readonly (string Label, string Scene)[] SceneOptions =
    {
        ("通用", "general"),
        ("开车", "driving"),
        ("做饭", "cooking"),
        ("健身", "fitness"),
    };

    // 延后（分钟，null 表示无）
    private static readonly TimeSpan?[] SnoozeValues =
    {
        null,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
    };

    // 预提醒（分钟，null 表示无）
    private static readonly TimeSpan?[] PreAlertValues =
    {
        null,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
    };

    public SettingsPage()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _provider = services.GetRequiredService<IModelProvider>();
        _backup = services.GetRequiredService<TodoBackupService>();
        _repo = services.GetRequiredService<ITodoRepository>();

        // 还原语言并即时生效（事件由 _initializing 守卫，避免初始化误写）
        var savedLang = AppSettings.Language;
        var culture = new CultureInfo(savedLang == "en" ? "en" : "zh");
        CoreStrings.CurrentCulture = culture;
        AppResources.CurrentCulture = culture;

        LangPicker.Items.Add("简体中文");
        LangPicker.Items.Add("English");
        PopulatePickers();
        PopulateModels();
        ApplyTexts();

        // 还原已保存设置
        LangPicker.SelectedIndex = savedLang == "en" ? 1 : 0;
        NagSwitch.IsToggled = AppSettings.NagMode;
        VoiceEntry.Text = Preferences.Default.Get("ttsVoice", "zh");

        // A6 动效
        AnimSwitch.IsToggled = AppSettings.UseAnimations;

        // B5 自定义提醒音 / B6 背景音乐共存
        CustomSoundEntry.Text = AppSettings.CustomSoundPath;
        AudioCoexistSwitch.IsToggled = AppSettings.AudioCoexist;

        // C7 静默时段（免打扰）：从 Preferences 还原，并联动时间选择器可用性
        QuietSwitch.IsToggled = Preferences.Default.Get("quiet.enabled", false);
        QuietFromPicker.Time = TimeSpan.FromTicks(Preferences.Default.Get("quiet.start", new TimeSpan(22, 0, 0).Ticks));
        QuietToPicker.Time = TimeSpan.FromTicks(Preferences.Default.Get("quiet.end", new TimeSpan(7, 0, 0).Ticks));
        QuietFromPicker.IsEnabled = QuietToPicker.IsEnabled = QuietSwitch.IsToggled;

        _initializing = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPermissionStatusAsync();
    }

    private static int IndexOfDuration(TimeSpan?[] values, TimeSpan? value)
    {
        if (value is null)
            return 0;
        for (var i = 0; i < values.Length; i++)
            if (values[i] == value)
                return i;
        return 0;
    }

    private static string DurationLabel(TimeSpan? value)
    {
        if (value is null)
            return AppResources.OptNone;
        var mins = (int)value.Value.TotalMinutes;
        return CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "zh" ? $"{mins} 分钟" : $"{mins} min";
    }

    private void PopulatePickers()
    {
        // B3 免提场景
        ScenePicker.Items.Clear();
        foreach (var (label, _) in SceneOptions)
            ScenePicker.Items.Add(label);
        var sceneIdx = Array.FindIndex(SceneOptions, o => o.Scene == AppSettings.Scene);
        ScenePicker.SelectedIndex = sceneIdx < 0 ? 0 : sceneIdx;

        foreach (var v in SnoozeValues)
            SnoozePicker.Items.Add(DurationLabel(v));
        SnoozePicker.SelectedIndex = IndexOfDuration(SnoozeValues, AppSettings.Snooze);

        foreach (var v in PreAlertValues)
            PreAlertPicker.Items.Add(DurationLabel(v));
        PreAlertPicker.SelectedIndex = IndexOfDuration(PreAlertValues, AppSettings.PreAlert);
    }

    private void PopulateModels()
    {
        _asrModels = _provider.GetModels(ModelKind.Asr).ToList();

        AsrModelPicker.Items.Clear();
        foreach (var m in _asrModels)
            AsrModelPicker.Items.Add(LabelOf(m));
        var activeAsr = _provider.GetActive(ModelKind.Asr);
        AsrModelPicker.SelectedIndex = Math.Max(0, _asrModels.FindIndex(m => m.Id == activeAsr.Id));
    }

    private static string LabelOf(ModelEntry m)
        => m.PackInBuild ? m.Name : $"{m.Name}（未随包，需切换打包）";

    private void ApplyTexts()
    {
        Title = AppResources.SettingsTitle;
        TitleLabel.Text = AppResources.SettingsTitle;
        GroupGeneralLabel.Text = AppResources.GroupGeneral;
        GroupSoundLabel.Text = AppResources.GroupSoundRemind;
        GroupExperienceLabel.Text = AppResources.GroupExperience;
        GroupPrivacyLabel.Text = AppResources.GroupPrivacy;
        GroupDataLabel.Text = AppResources.DataGroup;
        ExportDataBtn.Text = "📦 " + AppResources.ExportData;
        ImportDataBtn.Text = "📥 " + AppResources.ImportData;
        ExportLogsBtn.Text = "🐞 " + AppResources.ExportLogs;

        LangLabel.Text = AppResources.Language;
        AsrModelLabel.Text = AppResources.AsrModelKey;

        ReminderSoundLabel.Text = AppResources.ReminderSound;
        TtsVoiceLabel.Text = AppResources.TtsVoice;
        NagLabel.Text = AppResources.NagMode;
        SnoozeLabel.Text = AppResources.DefaultSnooze;
        PreAlertLabel.Text = "预提醒";
        CustomSoundEntry.Placeholder = AppResources.OptNone;

        // C7 静默时段（免打扰）
        QuietHoursLabel.Text = AppResources.QuietHours;
        QuietFromLabel.Text = AppResources.QuietFrom;
        QuietToLabel.Text = AppResources.QuietTo;
        QuietHintLabel.Text = AppResources.QuietHint;

        AnimLabel.Text = AppResources.InterfaceAnim;
        SceneLabel.Text = AppResources.HandsFreeScene;
        AudioCoexistLabel.Text = "背景音乐共存";

        MicLabel.Text = AppResources.MicLabel;
        NotifLabel.Text = AppResources.NotifLabel;
        AutoSaveHintLabel.Text = AppResources.AutoSaveHint;

        if (_micStatus is { } mic)
            MicValue.Text = mic == PermissionStatus.Granted ? AppResources.Allowed : mic.ToString();
        if (_notifStatus is { } notif)
            NotifValue.Text = notif == PermissionStatus.Granted ? AppResources.Allowed : notif.ToString();
    }

    private async Task LoadPermissionStatusAsync()
    {
        try
        {
            _micStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            MicRow.IsVisible = true;
        }
        catch (Exception ex)
        {
            // C12：保留原降级行为，但把失败记入诊断日志，不再无声吞掉
            AppLog.Warn($"麦克风权限状态读取失败: {ex.Message}");
            _micStatus = null;
            MicRow.IsVisible = false;
        }

#if ANDROID
        try
        {
            _notifStatus = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            NotifRow.IsVisible = true;
        }
        catch (Exception ex)
        {
            // C12：同上
            AppLog.Warn($"通知权限状态读取失败: {ex.Message}");
            _notifStatus = null;
            NotifRow.IsVisible = false;
        }
#else
        _notifStatus = PermissionStatus.Granted;
        NotifRow.IsVisible = true;
#endif

        PermSeparator.IsVisible = MicRow.IsVisible && NotifRow.IsVisible;
        ApplyTexts();
    }

    private void OnLangChanged(object sender, EventArgs e)
    {
        if (_initializing || LangPicker.SelectedIndex < 0)
            return;
        var lang = LangPicker.SelectedIndex == 0 ? "zh" : "en";
        var culture = new CultureInfo(lang);
        CoreStrings.CurrentCulture = culture;
        AppResources.CurrentCulture = culture;
        AppSettings.Language = lang; // 持久化（启动时由 AppSettings.Load 回读）

        // 按新文化重建选项文案（选中值以 AppSettings 为准）
        PopulatePickers();
        ApplyTexts();
    }

    private void OnAsrModelChanged(object sender, EventArgs e)
    {
        if (_initializing || AsrModelPicker.SelectedIndex < 0) return;
        var entry = _asrModels[AsrModelPicker.SelectedIndex];
        _provider.SetActive(ModelKind.Asr, entry.Id);
    }

    private void OnTtsVoiceChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        Preferences.Default.Set("ttsVoice", string.IsNullOrWhiteSpace(e.NewTextValue) ? "zh" : e.NewTextValue.Trim());
    }

    private void OnAnimToggled(object sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        AppSettings.UseAnimations = e.Value; // 持久化
    }

    private void OnNagToggled(object sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        AppSettings.NagMode = e.Value; // 持久化
    }

    private void OnAudioCoexistToggled(object sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        AppSettings.AudioCoexist = e.Value; // 持久化
    }

    private void OnSceneChanged(object sender, EventArgs e)
    {
        if (_initializing || ScenePicker.SelectedIndex < 0 || ScenePicker.SelectedIndex >= SceneOptions.Length) return;
        AppSettings.Scene = SceneOptions[ScenePicker.SelectedIndex].Scene; // 持久化
    }

    private void OnSnoozeChanged(object sender, EventArgs e)
    {
        if (_initializing || SnoozePicker.SelectedIndex < 0 || SnoozePicker.SelectedIndex >= SnoozeValues.Length) return;
        AppSettings.Snooze = SnoozeValues[SnoozePicker.SelectedIndex]; // 持久化
    }

    private void OnPreAlertChanged(object sender, EventArgs e)
    {
        if (_initializing || PreAlertPicker.SelectedIndex < 0 || PreAlertPicker.SelectedIndex >= PreAlertValues.Length) return;
        AppSettings.PreAlert = PreAlertValues[PreAlertPicker.SelectedIndex]; // 持久化
    }

    private void OnCustomSoundChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        AppSettings.CustomSoundPath = string.IsNullOrWhiteSpace(e.NewTextValue) ? null : e.NewTextValue.Trim();
    }

    // ══════════ C7 静默时段（免打扰） ══════════

    /// <summary>开关：持久化到 Preferences（不用 AppSettings 静态字段，避免重启丢失），并联动时间选择器可用性。</summary>
    private void OnQuietToggled(object sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        Preferences.Default.Set("quiet.enabled", e.Value);
        QuietFromPicker.IsEnabled = QuietToPicker.IsEnabled = e.Value;
    }

    /// <summary>起/止时间变更：仅 Time 属性变化时持久化到 Preferences；初始化阶段由 _initializing 守卫跳过。</summary>
    private void OnQuietTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_initializing || e.PropertyName != nameof(TimePicker.Time)) return;
        if (QuietFromPicker.Time is { } from && QuietToPicker.Time is { } to)
        {
            Preferences.Default.Set("quiet.start", from.Ticks);
            Preferences.Default.Set("quiet.end", to.Ticks);
        }
    }

    // ══════════ C2 数据导出 / 导入 ══════════

    /// <summary>导出全部数据并分享：写 JSON 到 CacheDirectory 后调起系统分享。</summary>
    private async void OnExportData(object? sender, EventArgs e)
    {
        try
        {
            var path = await _backup.ExportAsync();
            var todos = await _repo.GetTodosAsync();
            var timers = await _repo.GetTimersAsync();
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = AppResources.ExportData,
                File = new ShareFile(path)
            });
            await UserAlerts.ShowAsync(AppResources.ExportData,
                string.Format(AppResources.ExportDoneFormat, todos.Count, timers.Count));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"导出数据失败: {ex.Message}");
            await UserAlerts.ShowAsync(AppResources.ExportData,
                string.Format(AppResources.ExportFailed, ex.Message));
        }
    }

    /// <summary>导入数据：选文件 → 逐条写入；用户取消选择（返回 null）静默返回。</summary>
    private async void OnImportData(object? sender, EventArgs e)
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/json" } },
                    { DevicePlatform.WinUI, new[] { ".json" } }
                })
            });
            if (pick is null) return; // 用户取消，不算错误

            var (todos, timers) = await _backup.ImportAsync(pick.FullPath);
            await UserAlerts.ShowAsync(AppResources.ImportData,
                string.Format(AppResources.ImportDoneFormat, todos, timers));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"导入数据失败: {ex.Message}");
            await UserAlerts.ShowAsync(AppResources.ImportData,
                string.Format(AppResources.ImportFailed, ex.Message));
        }
    }

    // ══════════ C12 导出诊断日志 ══════════

    /// <summary>把诊断日志合并导出并分享；无日志时提示。</summary>
    private async void OnExportLogs(object? sender, EventArgs e)
    {
        try
        {
            var path = await AppLog.ExportAsync();
            if (path is null)
            {
                await UserAlerts.ShowAsync(AppResources.ExportLogs, AppResources.LogsNone);
                return;
            }
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = AppResources.ExportLogs,
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"导出诊断日志失败: {ex.Message}");
            await UserAlerts.ShowAsync(AppResources.ExportLogs,
                string.Format(AppResources.ExportFailed, ex.Message));
        }
    }
}
