using System.Globalization;
using System.Resources;

namespace VoiceTodo.Maui.Resources;

/// <summary>
/// MAUI 壳 UI 文案本地化。中性（英文）在 AppResources.resx，中文卫星在 AppResources.zh.resx。
/// 新增语言只需补 .resx，无需改逻辑（与 Core 双层 RESX 一致）。
/// </summary>
public static class AppResources
{
    private static readonly ResourceManager _rm =
        new("VoiceTodo.Maui.Resources.AppResources", typeof(AppResources).Assembly);

    private static CultureInfo? _culture;

    public static CultureInfo CurrentCulture
    {
        get => _culture ?? CultureInfo.CurrentUICulture;
        set => _culture = value;
    }

    public static string Get(string key) => _rm.GetString(key, CurrentCulture) ?? key;

    public static string AppTitle => Get(nameof(AppTitle));
    public static string MainTitle => Get(nameof(MainTitle));
    public static string TimerTitle => Get(nameof(TimerTitle));
    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string Listen => Get(nameof(Listen));
    public static string Listening => Get(nameof(Listening));
    public static string LatestCommand => Get(nameof(LatestCommand));
    public static string Todos => Get(nameof(Todos));
    public static string Timers => Get(nameof(Timers));
    public static string Language => Get(nameof(Language));
    public static string UseChinese => Get(nameof(UseChinese));
    public static string UseEnglish => Get(nameof(UseEnglish));
    public static string NagMode => Get(nameof(NagMode));
    public static string TtsVoice => Get(nameof(TtsVoice));
    public static string Save => Get(nameof(Save));
    public static string Back => Get(nameof(Back));
    public static string StartTimer => Get(nameof(StartTimer));
    public static string StopTimer => Get(nameof(StopTimer));
    public static string PresetTabata => Get(nameof(PresetTabata));
    public static string PresetHiit => Get(nameof(PresetHiit));
    public static string PresetCircuit => Get(nameof(PresetCircuit));
    public static string Status => Get(nameof(Status));
    public static string CalendarTitle => Get(nameof(CalendarTitle));
    public static string Today => Get(nameof(Today));
    public static string MonthView => Get(nameof(MonthView));
    public static string WeekView => Get(nameof(WeekView));
    public static string DayTodos => Get(nameof(DayTodos));
    public static string NoTodosDay => Get(nameof(NoTodosDay));
    public static string OpenCalendar => Get(nameof(OpenCalendar));
    public static string TodayTitle => Get(nameof(TodayTitle));
    public static string SummaryFormat => Get(nameof(SummaryFormat));
    public static string ViewAll => Get(nameof(ViewAll));
    public static string CompletedCount => Get(nameof(CompletedCount));
    public static string SayToRecord => Get(nameof(SayToRecord));
    public static string TypeToAdd => Get(nameof(TypeToAdd));
    public static string TextAddTitle => Get(nameof(TextAddTitle));
    public static string TextAddPlaceholder => Get(nameof(TextAddPlaceholder));
    public static string Cancel => Get(nameof(Cancel));
    public static string OK => Get(nameof(OK));
    public static string RunningNow => Get(nameof(RunningNow));
    public static string SearchAction => Get(nameof(SearchAction));
    public static string ListsTitle => Get(nameof(ListsTitle));
    public static string SearchTask => Get(nameof(SearchTask));
    public static string FilterUnscheduled => Get(nameof(FilterUnscheduled));
    public static string FilterToday => Get(nameof(FilterToday));
    public static string FilterUpcoming => Get(nameof(FilterUpcoming));
    public static string FilterCompleted => Get(nameof(FilterCompleted));
    public static string UnscheduledHint => Get(nameof(UnscheduledHint));
    public static string NoTimeLabel => Get(nameof(NoTimeLabel));
    public static string SearchEmpty => Get(nameof(SearchEmpty));
    public static string EmptyUnscheduled => Get(nameof(EmptyUnscheduled));
    public static string EmptyToday => Get(nameof(EmptyToday));
    public static string EmptyUpcoming => Get(nameof(EmptyUpcoming));
    public static string EmptyCompleted => Get(nameof(EmptyCompleted));
    public static string TimersTitle => Get(nameof(TimersTitle));
    public static string Countdown => Get(nameof(Countdown));
    public static string IntervalTraining => Get(nameof(IntervalTraining));
    public static string InProgress => Get(nameof(InProgress));
    public static string CommonTimers => Get(nameof(CommonTimers));
    public static string RecentUsed => Get(nameof(RecentUsed));
    public static string NewTimer => Get(nameof(NewTimer));
    public static string Pause => Get(nameof(Pause));
    public static string Resume => Get(nameof(Resume));
    public static string Finish => Get(nameof(Finish));
    public static string RoundFormat => Get(nameof(RoundFormat));
    public static string NextUp => Get(nameof(NextUp));
    public static string VoiceHintOn => Get(nameof(VoiceHintOn));
    public static string QuickMinFormat => Get(nameof(QuickMinFormat));
    public static string EndConfirmTitle => Get(nameof(EndConfirmTitle));
    public static string EndConfirmText => Get(nameof(EndConfirmText));
    public static string NoRunning => Get(nameof(NoRunning));
    public static string EmptyTimers => Get(nameof(EmptyTimers));
    public static string TotalLabel => Get(nameof(TotalLabel));
    public static string CalUnscheduled => Get(nameof(CalUnscheduled));
    public static string CalUnscheduledEmpty => Get(nameof(CalUnscheduledEmpty));
    public static string GroupGeneral => Get(nameof(GroupGeneral));
    public static string GroupSoundRemind => Get(nameof(GroupSoundRemind));
    public static string GroupExperience => Get(nameof(GroupExperience));
    public static string GroupPrivacy => Get(nameof(GroupPrivacy));
    public static string ReminderSound => Get(nameof(ReminderSound));
    public static string DefaultSnooze => Get(nameof(DefaultSnooze));
    public static string VoiceBroadcast => Get(nameof(VoiceBroadcast));
    public static string AsrModelKey => Get(nameof(AsrModelKey));
    public static string InterfaceAnim => Get(nameof(InterfaceAnim));
    public static string HandsFreeScene => Get(nameof(HandsFreeScene));
    public static string MicLabel => Get(nameof(MicLabel));
    public static string NotifLabel => Get(nameof(NotifLabel));
    public static string Allowed => Get(nameof(Allowed));
    public static string VoiceDataEntry => Get(nameof(VoiceDataEntry));
    public static string AutoSaveHint => Get(nameof(AutoSaveHint));
    public static string OptNone => Get(nameof(OptNone));
    public static string EditorTitle => Get(nameof(EditorTitle));
    public static string FieldContent => Get(nameof(FieldContent));
    public static string FieldDate => Get(nameof(FieldDate));
    public static string FieldTime => Get(nameof(FieldTime));
    public static string FieldReminder => Get(nameof(FieldReminder));
    public static string FieldRepeat => Get(nameof(FieldRepeat));
    public static string FieldNotes => Get(nameof(FieldNotes));
    public static string RepeatNone => Get(nameof(RepeatNone));
    public static string RepeatDaily => Get(nameof(RepeatDaily));
    public static string RepeatWeekly => Get(nameof(RepeatWeekly));
    public static string OnTimeRemind => Get(nameof(OnTimeRemind));
    public static string Early10 => Get(nameof(Early10));
    public static string Early30 => Get(nameof(Early30));
    public static string NoDueLabel => Get(nameof(NoDueLabel));
    public static string SaveChanges => Get(nameof(SaveChanges));
    public static string TodayWord => Get(nameof(TodayWord));
    public static string TomorrowWord => Get(nameof(TomorrowWord));
    public static string DeleteTask => Get(nameof(DeleteTask));
    public static string NotesPlaceholder => Get(nameof(NotesPlaceholder));
    public static string VoiceRecordTitle => Get(nameof(VoiceRecordTitle));
    public static string ListeningNow => Get(nameof(ListeningNow));
    public static string FinishRecord => Get(nameof(FinishRecord));
    public static string SpeakHint => Get(nameof(SpeakHint));
    public static string SwitchToType => Get(nameof(SwitchToType));
    public static string ConfirmRecordTitle => Get(nameof(ConfirmRecordTitle));
    public static string YouSaid => Get(nameof(YouSaid));
    public static string OrganizedBanner => Get(nameof(OrganizedBanner));
    public static string ItemLabel => Get(nameof(ItemLabel));
    public static string TapRowHint => Get(nameof(TapRowHint));
    public static string ConfirmAdd => Get(nameof(ConfirmAdd));
    public static string SayAgain => Get(nameof(SayAgain));
    public static string InputPlaceholder => Get(nameof(InputPlaceholder));
    public static string RecognizedHeader => Get(nameof(RecognizedHeader));
    public static string EditHintText => Get(nameof(EditHintText));
    public static string VoiceFailText => Get(nameof(VoiceFailText));
    public static string RecognizingText => Get(nameof(RecognizingText));
    public static string NewTimerTitle => Get(nameof(NewTimerTitle));
    public static string TimerNameLabel => Get(nameof(TimerNameLabel));
    public static string TemplateHeader => Get(nameof(TemplateHeader));
    public static string TplCustom => Get(nameof(TplCustom));
    public static string WorkLabel => Get(nameof(WorkLabel));
    public static string RestLabel => Get(nameof(RestLabel));
    public static string RoundsLabel => Get(nameof(RoundsLabel));
    public static string PreviewHeader => Get(nameof(PreviewHeader));
    public static string TotalFmt => Get(nameof(TotalFmt));
    public static string TtsCueLabel => Get(nameof(TtsCueLabel));
    public static string TtsCueSub => Get(nameof(TtsCueSub));
    public static string SaveStartBtn => Get(nameof(SaveStartBtn));
    public static string SaveOnlyBtn => Get(nameof(SaveOnlyBtn));
    public static string WorkLegend => Get(nameof(WorkLegend));
    public static string RestLegend => Get(nameof(RestLegend));
    public static string TimerNamePlaceholder => Get(nameof(TimerNamePlaceholder));
    public static string DueRemindTitle => Get(nameof(DueRemindTitle));
    public static string TimeArrived => Get(nameof(TimeArrived));
    public static string SnoozeHeader => Get(nameof(SnoozeHeader));
    public static string CustomTimeLink => Get(nameof(CustomTimeLink));
    public static string MarkDoneBtn => Get(nameof(MarkDoneBtn));
    public static string SnoozeApplyBtn => Get(nameof(SnoozeApplyBtn));
    public static string OnbHead => Get(nameof(OnbHead));
    public static string OnbSub => Get(nameof(OnbSub));
    public static string OnbQuote => Get(nameof(OnbQuote));
    public static string OnbResult => Get(nameof(OnbResult));
    public static string OnbF1Head => Get(nameof(OnbF1Head));
    public static string OnbF1Sub => Get(nameof(OnbF1Sub));
    public static string OnbF2Head => Get(nameof(OnbF2Head));
    public static string OnbF2Sub => Get(nameof(OnbF2Sub));
    public static string OnbFine => Get(nameof(OnbFine));
    public static string GetStartedBtn => Get(nameof(GetStartedBtn));
    public static string TextFirstBtn => Get(nameof(TextFirstBtn));

    // L2 主链路：语音建组确认 / 计时留痕 / 日历记录图层
    public static string OrganizedTimerBanner => Get(nameof(OrganizedTimerBanner));
    public static string FieldPlan => Get(nameof(FieldPlan));
    public static string RoundsCount => Get(nameof(RoundsCount));
    public static string PhaseWorkShort => Get(nameof(PhaseWorkShort));
    public static string PhaseRestShort => Get(nameof(PhaseRestShort));
    public static string TimerSessions => Get(nameof(TimerSessions));
    public static string SessionCompleted => Get(nameof(SessionCompleted));
    public static string SessionCancelled => Get(nameof(SessionCancelled));
    public static string RenameSessionTitle => Get(nameof(RenameSessionTitle));
    public static string SessionNamePrompt => Get(nameof(SessionNamePrompt));

    // DEV-02 remind
    public static string SavedRemindOn => Get(nameof(SavedRemindOn));
    public static string SavedRemindOffFormat => Get(nameof(SavedRemindOffFormat));

    // DEV-03 mic：真实采集 / 权限三分支 / 完成本句与取消本句
    public static string FinishSentence => Get(nameof(FinishSentence));
    public static string DiscardSentence => Get(nameof(DiscardSentence));
    public static string MicAwaitText => Get(nameof(MicAwaitText));
    public static string MicDeniedText => Get(nameof(MicDeniedText));
    public static string MicOpenSettings => Get(nameof(MicOpenSettings));
    public static string MicUnavailableText => Get(nameof(MicUnavailableText));
    public static string MicRetry => Get(nameof(MicRetry));
    public static string ErrorDraftPrefix => Get(nameof(ErrorDraftPrefix));

    // DEV-05 run：全程剩余 / 中断恢复 / 纠错停止与编辑 / 阶段播报
    public static string TotalLeftFmt => Get(nameof(TotalLeftFmt));
    public static string InterruptedRun => Get(nameof(InterruptedRun));
    public static string FixMistakeBtn => Get(nameof(FixMistakeBtn));
    public static string FixConfirmTitle => Get(nameof(FixConfirmTitle));
    public static string FixConfirmText => Get(nameof(FixConfirmText));
    public static string SessionInterrupted => Get(nameof(SessionInterrupted));
    public static string RoundAnnounceFmt => Get(nameof(RoundAnnounceFmt));
    public static string RestAnnounceFmt => Get(nameof(RestAnnounceFmt));

    // DEV-04 plan：通用计划编辑器 / 口述追问 / 缺槽提示
    public static string EditPlanTitle => Get(nameof(EditPlanTitle));
    public static string ActionNameLabel => Get(nameof(ActionNameLabel));
    public static string PlanActionPlaceholder => Get(nameof(PlanActionPlaceholder));
    public static string RestLastLabel => Get(nameof(RestLastLabel));
    public static string CountdownWindowLabel => Get(nameof(CountdownWindowLabel));
    public static string PlanWindow5 => Get(nameof(PlanWindow5));
    public static string PlanWindow3 => Get(nameof(PlanWindow3));
    public static string PlanWindowOff => Get(nameof(PlanWindowOff));
    public static string PhaseAnnounceLabel => Get(nameof(PhaseAnnounceLabel));
    public static string PhaseAnnounceSub => Get(nameof(PhaseAnnounceSub));
    public static string PlanFollowUpTitle => Get(nameof(PlanFollowUpTitle));
    public static string PlanMissingFormat => Get(nameof(PlanMissingFormat));
    public static string PlanMissingAction => Get(nameof(PlanMissingAction));
    public static string PlanMissingRounds => Get(nameof(PlanMissingRounds));
    public static string PlanMissingRoundsCap => Get(nameof(PlanMissingRoundsCap));
    public static string PlanMissingWork => Get(nameof(PlanMissingWork));
    public static string PlanMissingRest => Get(nameof(PlanMissingRest));
    public static string PlanCountHint => Get(nameof(PlanCountHint));
    public static string PlanFollowUpHint => Get(nameof(PlanFollowUpHint));
    public static string PlanSpeakAgain => Get(nameof(PlanSpeakAgain));
    public static string PlanManualEdit => Get(nameof(PlanManualEdit));

    // 可靠性地基：存储异常提示 / 留痕失败 / 常驻会话
    public static string StorageErrorTitle => Get(nameof(StorageErrorTitle));
    public static string StorageErrorMessage => Get(nameof(StorageErrorMessage));
    public static string Retry => Get(nameof(Retry));
    public static string SessionSaveFailedFormat => Get(nameof(SessionSaveFailedFormat));
    public static string EndSession => Get(nameof(EndSession));

    // A1 通知权限（从未运行时申请 → 首次创建带提醒待办 / 引导完成时申请）
    public static string Perm_RemindOffNoNotif => Get(nameof(Perm_RemindOffNoNotif));

    // A2 运行中训练冲突：替换 / 取消
    public static string Conflict_Title => Get(nameof(Conflict_Title));
    public static string Conflict_Text => Get(nameof(Conflict_Text));
    public static string Conflict_Replace => Get(nameof(Conflict_Replace));
    public static string Conflict_Cancel => Get(nameof(Conflict_Cancel));
    public static string Conflict_Kept => Get(nameof(Conflict_Kept));

    // A4 按任务停止催促
    public static string NagStop_Button => Get(nameof(NagStop_Button));
    public static string NagStop_Done => Get(nameof(NagStop_Done));

    // C 组功能（feature-builder-b）文案
    // C1 删除 + 撤销
    public static string Undo_DeletedFormat => Get(nameof(Undo_DeletedFormat));
    public static string Undo_Action => Get(nameof(Undo_Action));
    public static string Undo_Failed => Get(nameof(Undo_Failed));

    // C2 数据导出 / 导入 / 分享
    public static string DataGroup => Get(nameof(DataGroup));
    public static string ExportData => Get(nameof(ExportData));
    public static string ImportData => Get(nameof(ImportData));
    public static string ExportDoneFormat => Get(nameof(ExportDoneFormat));
    public static string ImportDoneFormat => Get(nameof(ImportDoneFormat));
    public static string ImportFailed => Get(nameof(ImportFailed));
    public static string ExportFailed => Get(nameof(ExportFailed));
    public static string ExportLogs => Get(nameof(ExportLogs));
    public static string LogsNone => Get(nameof(LogsNone));

    // C4 自定义训练模板
    public static string SaveAsTemplate => Get(nameof(SaveAsTemplate));
    public static string TplNamePrompt => Get(nameof(TplNamePrompt));
    public static string TplSavedFormat => Get(nameof(TplSavedFormat));
    public static string TplDeleteConfirm => Get(nameof(TplDeleteConfirm));
    public static string TplLimitReached => Get(nameof(TplLimitReached));
    public static string SaveAsTemplateTitle => Get(nameof(SaveAsTemplateTitle));
    public static string TplDeleteTitle => Get(nameof(TplDeleteTitle));

    // C5 清单页新建入口
    public static string AddTodo => Get(nameof(AddTodo));
}
