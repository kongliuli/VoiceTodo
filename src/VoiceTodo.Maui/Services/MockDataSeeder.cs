using System.Text.Json;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 测试数据种子（仅 DEBUG 生效）：样例库与真实主库物理隔离——30 条样例写入独立模板文件
/// voicetodo.sample.json（同目录），仅当主库 voicetodo.json 不存在时，先刷新样例模板再复制为主库
/// 完成首次初始化；主库已存在（含损坏、空库）时完全不碰，避免样例污染真实数据。
/// 删除主库文件后重启可再次初始化。
/// </summary>
public static class MockDataSeeder
{
    /// <summary>样例模板文件名，与主库 voicetodo.json 同目录、物理分离。</summary>
    private const string SampleFileName = "voicetodo.sample.json";

    private static readonly object _initLock = new();

    /// <summary>样例库结构，与 JsonFileTodoRepository 的存储格式字段保持一致。</summary>
    private sealed record SampleStore
    {
        public int NextId { get; set; } = 1;
        public List<TodoItem> Todos { get; set; } = new();
        public List<TimerItem> Timers { get; set; } = new();
        public List<TimerSession> Sessions { get; set; } = new();
    }

    /// <summary>
    /// 保留原签名（MainPage #if DEBUG 调用形态不变）：主库文件不存在时用样例模板完成首次初始化。
    /// </summary>
    public static Task SeedIfEmptyAsync(ITodoRepository repo)
    {
        try
        {
            Initialize();
        }
        catch
        {
            // 种子失败不阻塞应用启动（与历史行为一致）
        }
        return Task.CompletedTask;
    }

    /// <summary>显式初始化：主库已存在则不碰；否则生成样例模板并复制为主库。</summary>
    public static void Initialize(string? directory = null)
    {
        lock (_initLock)
        {
            var dir = directory ?? JsonFileTodoRepository.GetDefaultDirectory();
            var mainPath = Path.Combine(dir, JsonFileTodoRepository.MainFileName);
            if (File.Exists(mainPath)) return; // 主库已存在：完全不碰

            // 先确保样例模板文件生成（每次重新生成，保证样例日期分布在当前一周内），再复制为主库
            var samplePath = Path.Combine(dir, SampleFileName);
            WriteSampleTemplate(samplePath);
            File.Copy(samplePath, mainPath, overwrite: false);
        }
    }

    /// <summary>生成 30 条样例并写入独立模板文件（样例内容逻辑与历史种子保持一致）。</summary>
    private static void WriteSampleTemplate(string samplePath)
    {
        var now = DateTimeOffset.Now;
        var today = DateTime.Today;
        var rnd = new Random();
        var list = new List<TodoItem>(30);

        string[] titles =
        {
            "晨跑 5 公里", "整理周报", "买牛奶和鸡蛋", "给绿植浇水", "回复客户邮件",
            "健身房力量训练", "阅读 30 页", "预约牙医", "缴水电费", "复盘本周计划",
            "洗碗机除垢", "剪头发", "给爸妈打电话", "准备季度汇报", "超市采购食材",
            "瑜伽拉伸 20 分钟", "清理收件箱", "修自行车刹车", "背 50 个单词", "和朋友约晚饭",
            "体检报告复查", "换床头灯灯泡", "写技术分享大纲", "退回不合适快递", "整理照片备份"
        };

        // 25 条：随机分布在今天起 7 天内，时段 7:00–21:45
        foreach (var title in titles)
        {
            int day = rnd.Next(0, 7);
            var due = new DateTimeOffset(
                today.AddDays(day).AddHours(rnd.Next(7, 22)).AddMinutes(rnd.Next(0, 4) * 15),
                now.Offset);
            list.Add(new TodoItem { Title = title, DueAt = due, CreatedAt = now });
        }

        // 3 条重复任务
        list.Add(new TodoItem
        {
            Title = "每天喝水 8 杯", IsRecurring = true, RecurrenceRule = "daily",
            DueAt = new DateTimeOffset(today.AddHours(9), now.Offset), CreatedAt = now
        });
        list.Add(new TodoItem
        {
            Title = "每周整理房间", IsRecurring = true, RecurrenceRule = "weekly",
            DueAt = new DateTimeOffset(FindNextWeekday(today, DayOfWeek.Friday).AddHours(10), now.Offset), CreatedAt = now
        });
        list.Add(new TodoItem
        {
            Title = "每周给车做保养检查", IsRecurring = true, RecurrenceRule = "weekly",
            DueAt = new DateTimeOffset(FindNextWeekday(today, DayOfWeek.Sunday).AddHours(16), now.Offset), CreatedAt = now
        });

        // 2 条待安排（无日期）
        list.Add(new TodoItem { Title = "某天去看新上的电影", CreatedAt = now });
        list.Add(new TodoItem { Title = "想读《卡拉马佐夫兄弟》", CreatedAt = now });

        // 已完成：把今天与更早到期的 5 条标记完成（当前日期偏移 0/1 的优先）
        var doneCount = 0;
        foreach (var item in list.Where(t => !t.IsRecurring && t.DueAt is { }).OrderBy(t => t.DueAt))
        {
            if (doneCount >= 5) break;
            item.IsDone = true;
            doneCount++;
        }

        // 分配 Id（与原先经 AddTodoAsync 分配一致：从 1 递增），NextId 落在 31
        for (int i = 0; i < list.Count; i++)
            list[i].Id = i + 1;

        var store = new SampleStore { NextId = list.Count + 1, Todos = list };
        File.WriteAllText(samplePath,
            JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>取今天之后（含今天）第一个指定星期几的日期。</summary>
    private static DateTime FindNextWeekday(DateTime from, DayOfWeek target)
    {
        int diff = ((int)target - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(diff);
    }
}
