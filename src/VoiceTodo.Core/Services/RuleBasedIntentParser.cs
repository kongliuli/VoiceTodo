using System.Globalization;
using System.Text.RegularExpressions;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 规则意图解析（MVP 采用，离线、确定性强、零模型体积）。
/// 通过关键词 / 正则判定 待办 或 定时器，并借助 ITimeParser 提取时长/相对时间/重复规则。
/// 后续可用端侧 SLM 替换而不影响上层。
/// </summary>
public class RuleBasedIntentParser : IIntentParser
{
    private readonly ITimeParser _timeParser;

    // 强定时器信号：显式定时器/闹钟词，或任何时长词（分钟/小时/秒）
    private static readonly string[] TimerStrong = { "定时器", "计时器", "计时", "timer", "alarm", "闹钟", "倒计时" };
    private static readonly string[] DurationWords = { "分钟", "分", "小时", "时", "秒", "min", "mins", "minute", "minutes", "hour", "hours", "hr", "hrs", "sec", "secs", "second", "seconds" };

    // 待办信号
    private static readonly string[] TodoWords = { "待办", "todo", "任务", "买", "做", "记一下", "记下", "task" };

    // 提醒信号：带相对时间的「N 分钟后提醒我X」是「待办 + 提醒时刻」，不是倒计时。
    // 优先级高于 DurationWords，否则「五分钟后提醒我关火」会因命中「分钟」被误判为 Timer。
    private static readonly string[] ReminderWords = { "提醒我", "提醒", "记得", "通知我", "催我", "催促" };

    // 动作信号（B1 语音管理：完成 / 删除 / 查询）
    private static readonly string[] CompleteWords = { "完成", "搞定", "办完", "done", "mark done", "finished", "complete" };
    private static readonly string[] DeleteWords = { "删除", "移除", "删掉", "去掉", "delete", "remove", "cancel" };
    private static readonly string[] QueryWords = { "查询", "看看", "有哪些", "列表", "清单", "what's on", "what do i", "show me", "list my", "remind what" };

    // 需要从标题中剔除的噪声词
    private static readonly string[] NoiseWords =
    {
        "提醒我", "催促我", "催促", "提醒", "通知我", "记得", "帮我", "请", "我想", "我要",
        "remind me", "reminder", "please", "i want to", "i need to", "tell me to",
        // B1 命令动作词（不进入标题）
        "完成", "搞定", "办完", "done", "mark done", "finished", "complete",
        "删除", "移除", "删掉", "去掉", "delete", "remove",
        "查询", "看看", "有哪些", "列表", "清单"
    };

    public RuleBasedIntentParser(ITimeParser timeParser)
    {
        _timeParser = timeParser;
    }

    public VoiceCommand Parse(string text, CultureInfo? culture = null)
    {
        text = (text ?? "").Trim();
        var cmd = new VoiceCommand { RawText = text };

        bool hasDuration = DurationWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool hasTimerStrong = TimerStrong.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool hasTodoWord = TodoWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool hasReminder = ReminderWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

        // 动作判定（B1）：完成 / 删除 / 查询 优先级高于新增
        bool hasComplete = CompleteWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool hasDelete = DeleteWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool hasQuery = QueryWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (hasQuery) cmd.Action = CommandAction.Query;
        else if (hasDelete) cmd.Action = CommandAction.Delete;
        else if (hasComplete) cmd.Action = CommandAction.Complete;

        // 判定类型。顺序：显式定时器词 > 提醒词 > 时长词 > 待办词 > 默认待办。
        // 提醒词先于时长词：「五分钟后提醒我关火」= 带提醒时刻的待办；「五分钟倒计时」才进 Timer。
        if (hasTimerStrong)
            cmd.Type = CommandType.Timer;
        else if (hasReminder)
            cmd.Type = CommandType.Todo;
        else if (hasDuration)
            cmd.Type = CommandType.Timer;
        else if (hasTodoWord)
            cmd.Type = CommandType.Todo;
        else
            cmd.Type = CommandType.Todo; // 默认归为待办（最常见）

        // 时间 / 时长 / 重复
        cmd.TriggerAt = _timeParser.ParseRelative(text, culture);
        if (_timeParser.TryParseDuration(text, out var dur)) cmd.Duration = dur;
        if (_timeParser.TryParseRecurrence(text, out var rule))
        {
            cmd.IsRecurring = true;
            cmd.RecurrenceRule = rule;
        }

        // DEV-04 统一计划：优先产出 Plan（Phases 由 Expand 派生，旧消费方平滑过渡）；
        // 缺槽不猜默认值，缺口记入 MissingSlots 交由追问。非计划口述走旧路径（如「五分钟倒计时」）。
        if (cmd.Action == CommandAction.Add && TryParseTrainingPlan(text, out var plan, out var missing, out var planTitle))
        {
            cmd.Type = CommandType.Timer;
            cmd.Plan = plan;
            cmd.MissingSlots = missing;
            cmd.Title = planTitle;
            if (plan is not null && plan.IsComplete && missing.Count == 0)
            {
                cmd.Phases = plan.Expand(); // 兼容旧消费方
                cmd.Duration = plan.TotalDuration;
            }
        }
        else
        {
            // 提取标题：去掉时长数字、噪声词
            cmd.Title = CleanTitle(text);
        }

        return cmd;
    }

    // ---- DEV-04：统一训练计划口述解析 ----

    /// <summary>缺失槽位标识（MissingSlots 内容，页面/TTS 据此追问，不猜默认值）。</summary>
    public const string SlotAction = "action";   // 动作名
    public const string SlotRounds = "rounds";   // 组数
    public const string SlotWork = "work";       // 每组运动时长
    public const string SlotRest = "rest";       // 组间休息时长（非必需，仅歧义时追问）
    public const string SlotCount = "count";     // 「N 次」计次语法：不支持，提示按组安排

    private const string Num = @"(?<num>\d+(?:\.\d+)?|半|[一二两三四五六七八九十]{1,3})";
    private const string Unit = @"(?<unit>小时|分钟|秒钟|秒|分|时)";

    // 组数：八组 / 8 轮 / 三遍（「N 次」≠「N 组」，不落入组数）
    private static readonly Regex RoundsWordRegex = new(Num + @"\s*(?:组|轮|遍)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 计次：5 次 / 十次（05：未支持计次训练，追问按时长安排）
    private static readonly Regex CountRegex = new(Num + @"\s*次", RegexOptions.Compiled);
    // 训练段：每组（持续/做/练）三十秒 / 每轮一分钟
    private static readonly Regex WorkEachRegex = new(@"每[组轮遍次]\s*(?:持续|做|练)?\s*" + Num + @"\s*" + Unit, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 无单位歧义：每组 5（缺「秒/分」，05：不猜单位，追问）
    private static readonly Regex WorkEachBareRegex = new(@"每[组轮遍]\s*" + Num + @"\s*(?![小时分钟秒钟秒分时])", RegexOptions.Compiled);
    // 训练段：20 秒运动 / 一分钟练
    private static readonly Regex WorkTailRegex = new(Num + @"\s*" + Unit + @"\s*(?:运动|练|work)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 训练段：运动三十秒 / 做 30 秒 / 持续 5 秒 / work 20 seconds
    private static readonly Regex WorkHeadRegex = new(@"(?:运动|练|做|持续|work)\s*" + Num + @"\s*" + Unit, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 休息段：休息十秒 / 歇半分钟
    private static readonly Regex RestHeadRegex = new(@"(?:休息|歇|rest)\s*" + Num + @"\s*" + Unit, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 无单位歧义：休息 5（缺单位）
    private static readonly Regex RestHeadBareRegex = new(@"(?:休息|歇|rest)\s*" + Num + @"\s*(?![小时分钟秒钟秒分时])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 休息段：15 秒休息
    private static readonly Regex RestTailRegex = new(Num + @"\s*" + Unit + @"\s*(?:休息|rest)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 修正句：「不是10组，是8组」→ 取修正值（05：否定修正更新草稿）
    private static readonly Regex CorrectionRegex = new(@"不是\s*" + Num + @"\s*(?:组|轮|遍|次)\s*[，,、]?\s*是\s*" + Num + @"\s*(?:组|轮|遍)", RegexOptions.Compiled);
    // 比例写法：hiit 20/10 × 8、tabata 30/15 x 6
    private static readonly Regex RatioRegex = new(
        @"(?<kw>hiit|hitt|tabata)\s*(?<work>\d+(?:\.\d+)?)\s*/\s*(?<rest>\d+(?:\.\d+)?)\s*[x×*]\s*(?<rounds>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 疑问/示例信号：出现即不触发创建（05：描述示例、询问不启动）
    private static readonly string[] QuestionMarkers = { "？", "?", "吗", "呢", "怎么", "如何", "什么", "比如", "例如", "示例", "举例" };

    private static readonly Dictionary<string, double> CnDigit = new()
    {
        ["一"] = 1, ["二"] = 2, ["两"] = 2, ["三"] = 3, ["四"] = 4,
        ["五"] = 5, ["六"] = 6, ["七"] = 7, ["八"] = 8, ["九"] = 9
    };

    /// <summary>
    /// 解析口述训练计划：「深蹲10组，每组持续5秒钟，休息10秒」→ 深蹲 · 10 组 · 运动 5 秒 · 休息 10 秒。
    /// 边界（05）：「10 次」≠「10 组」→ SlotCount；无单位数字 → 对应槽位歧义；缺动作名/组数/每组时长 → MissingSlots；
    /// 疑问/示例句不触发；组间休息可选（缺省 0，不追问）。未命中计划形态返回 false（走旧路径）。
    /// </summary>
    private static bool TryParseTrainingPlan(string text, out TrainingPlan? plan, out List<string> missing, out string title)
    {
        plan = null;
        title = "";
        missing = new List<string>();

        // 疑问 / 示例句不触发创建
        if (QuestionMarkers.Any(text.Contains)) return false;

        // 1) hiit / tabata 比例写法（完整计划，动作名取关键词）
        var rm = RatioRegex.Match(text);
        if (rm.Success)
        {
            var work = double.Parse(rm.Groups["work"].Value, CultureInfo.InvariantCulture);
            var rest = double.Parse(rm.Groups["rest"].Value, CultureInfo.InvariantCulture);
            var rounds = int.TryParse(rm.Groups["rounds"].Value, out var n) ? Math.Clamp(n, 1, 99) : 1;
            plan = new TrainingPlan
            {
                ActionName = rm.Groups["kw"].Value.ToUpperInvariant(),
                Rounds = rounds,
                WorkDuration = TimeSpan.FromSeconds(work),
                RestDuration = TimeSpan.FromSeconds(rest)
            };
            title = plan.ActionName;
            return true;
        }

        // 2) 修正句优先：「不是10组，是8组」→ 组数取修正值
        var corr = CorrectionRegex.Match(text);
        int? correctedRounds = null;
        if (corr.Success)
        {
            var v = ParseCnNum(corr.Groups["num"].Value);
            if (v is not null) correctedRounds = (int)Math.Clamp(Math.Round(v.Value), 1, 99);
        }

        // 3) 信号词：组数 / 计次 / 「每组」写法 —— 命中才进入计划分支（否则保持旧路径）
        var roundsM = FindRounds(text, skipFirst: corr.Success);
        var countM = CountRegex.Match(text);
        var workEachM = WorkEachRegex.Match(text);
        var workEachBareM = WorkEachBareRegex.Match(text);
        bool hasRounds = roundsM.Success || correctedRounds is not null;
        bool engaged = hasRounds || countM.Success || workEachM.Success || workEachBareM.Success;
        if (!engaged) return false;

        plan = new TrainingPlan();

        // 组数（缺失/计次不猜默认值：Rounds 置 0 使 IsComplete=false）
        if (correctedRounds is { } cr) plan.Rounds = cr;
        else if (roundsM.Success)
        {
            var v = ParseCnNum(roundsM.Groups["num"].Value);
            if (v is null) missing.Add(SlotRounds);
            else plan.Rounds = (int)Math.Clamp(Math.Round(v.Value), 1, 99);
        }
        else if (countM.Success) missing.Add(SlotCount);
        else missing.Add(SlotRounds);
        if (plan.Rounds < 1) plan.Rounds = 0;

        // 每组运动时长（必需）：优先「每组N单位」，其次 N单位运动 / 运动N单位
        var workM = FirstMatch(workEachM, WorkTailRegex.Match(text), WorkHeadRegex.Match(text));
        if (workM is { } wm)
        {
            var sec = ParseCnNum(wm.Groups["num"].Value);
            if (sec is null) missing.Add(SlotWork);
            else plan.WorkDuration = TimeSpan.FromSeconds(sec.Value * UnitSeconds(wm.Groups["unit"].Value));
        }
        else missing.Add(SlotWork); // 含「每组 5」无单位歧义 → 追问，不猜单位

        // 组间休息（可选，缺省 0；出现但无单位 → 歧义追问）
        var restM = FirstMatch(RestHeadRegex.Match(text), RestTailRegex.Match(text));
        if (restM is { } rm2)
        {
            var sec = ParseCnNum(rm2.Groups["num"].Value);
            if (sec is null) missing.Add(SlotRest);
            else plan.RestDuration = TimeSpan.FromSeconds(sec.Value * UnitSeconds(rm2.Groups["unit"].Value));
        }
        else if (RestHeadBareRegex.Match(text).Success) missing.Add(SlotRest);

        // 动作名 = 去掉数量词与阶段短语后的主题文本（缺失不猜「间歇训练」默认名）
        title = ExtractActionName(text);
        if (string.IsNullOrWhiteSpace(title))
        {
            plan.ActionName = "";
            missing.Add(SlotAction);
        }
        else plan.ActionName = title;

        return true;
    }

    /// <summary>取组数匹配；skipFirst 时跳过首个命中（修正句「不是N组」已由 CorrectionRegex 处理）。</summary>
    private static Match FindRounds(string text, bool skipFirst)
    {
        var matches = RoundsWordRegex.Matches(text);
        foreach (Match m in matches)
        {
            if (skipFirst)
            {
                skipFirst = false;
                continue;
            }
            // 跳过否定语境「不是N组」（无修正值时不猜）
            if (m.Index >= 2 && text.Substring(m.Index - 2, 2) == "不是") continue;
            return m;
        }
        return Match.Empty;
    }

    private static Match? FirstMatch(params Match[] matches) =>
        matches.FirstOrDefault(m => m.Success);

    /// <summary>动作名：剔除修正句、组数/计次词与阶段短语后的主题文本。</summary>
    private static string ExtractActionName(string text)
    {
        var t = CorrectionRegex.Replace(text, " ");
        t = RoundsWordRegex.Replace(t, " ");
        t = CountRegex.Replace(t, " ");
        t = WorkEachRegex.Replace(t, " ");
        t = WorkEachBareRegex.Replace(t, " ");
        t = WorkTailRegex.Replace(t, " ");
        t = WorkHeadRegex.Replace(t, " ");
        t = RestHeadRegex.Replace(t, " ");
        t = RestHeadBareRegex.Replace(t, " ");
        t = RestTailRegex.Replace(t, " ");
        foreach (var w in new[] { "每组", "每轮", "每遍", "持续", "做", "练" })
            t = t.Replace(w, "");
        return CleanTitle(t);
    }

    /// <summary>解析阿拉伯数字 / 中文数词（一~十、两、含「十」的两位数、半）。</summary>
    private static double? ParseCnNum(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (s == "半") return 0.5;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        if (s == "十") return 10;
        var idx = s.IndexOf('十');
        if (idx < 0)
            return s.Length == 1 && CnDigit.TryGetValue(s, out var v) ? v : null;
        var tens = idx == 0 ? 1 : CnDigit.TryGetValue(s[..idx], out var t) ? t : -1;
        var ones = idx == s.Length - 1 ? 0 : CnDigit.TryGetValue(s[(idx + 1)..], out var o) ? o : -1;
        if (tens < 0 || ones < 0) return null;
        return tens * 10 + ones;
    }

    private static double UnitSeconds(string unit) => unit switch
    {
        "秒" or "秒钟" => 1,
        "分" or "分钟" => 60,
        "小时" or "时" => 3600,
        _ => 0
    };

    private static string CleanTitle(string text)
    {
        var result = text;
        // 去掉 "数字+时长单位"
        result = System.Text.RegularExpressions.Regex.Replace(result,
            @"\d+(?:\.\d+)?\s*(分钟|分|小时|时|秒|min|mins|minute|minutes|hour|hours|hr|hrs|sec|secs|second|seconds)",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // 去掉相对时间词
        result = System.Text.RegularExpressions.Regex.Replace(result,
            @"(后天|明天|今天|大后天|每天早上?|每天晚上?|每?周[一二三四五六日天]|星期[一二三四五六日天]|every\s+(day|monday|tuesday|wednesday|thursday|friday|saturday|sunday)|\d+\s*(分钟|小时|秒)?\s*后\b)",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var n in NoiseWords)
            result = result.Replace(n, "", StringComparison.OrdinalIgnoreCase);

        return result.Trim(' ', '，', ',', '。', '.', '的');
    }
}
