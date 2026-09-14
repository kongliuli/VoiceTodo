using System.Text.Json;
using Microsoft.Maui.Storage;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 自定义训练模板（C4）：用 Preferences 存一个 JSON 数组，供用户在 Tabata/HIIT 之外保存并复用参数组合。
/// 同名覆盖；上限 8 个，达到上限且为新名字时 Save 返回 false（调用方提示已达上限，不静默丢数据）。
/// </summary>
public sealed class TemplateStore
{
    private const string Key = "customTemplates";
    private const int MaxCount = 8;

    public List<TrainingTemplate> Load()
    {
        try
        {
            var json = Preferences.Default.Get(Key, "");
            if (string.IsNullOrWhiteSpace(json)) return new List<TrainingTemplate>();
            var list = JsonSerializer.Deserialize<List<TrainingTemplate>>(json);
            return list ?? new List<TrainingTemplate>();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"训练模板读取失败: {ex.Message}");
            return new List<TrainingTemplate>();
        }
    }

    /// <summary>保存模板：同名覆盖；未达上限则追加；已达上限且为新名字则拒绝并返回 false。</summary>
    public bool Save(string name, TrainingTemplate tpl)
    {
        var list = Load();
        var existing = list.FirstOrDefault(t => t.Name == name);
        if (existing is not null)
        {
            list.Remove(existing); // 同名覆盖，不计入上限
        }
        else if (list.Count >= MaxCount)
        {
            return false; // 达上限，拒绝新增
        }

        tpl.Name = name;
        list.Add(tpl);
        Persist(list);
        return true;
    }

    public void Delete(string name)
    {
        var list = Load();
        list.RemoveAll(t => t.Name == name);
        Persist(list);
    }

    private static void Persist(List<TrainingTemplate> list)
    {
        try
        {
            Preferences.Default.Set(Key, JsonSerializer.Serialize(list));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"训练模板保存失败: {ex.Message}");
        }
    }
}

/// <summary>训练模板参数快照（与 TimerEditorPage 的 _workSec/_restSec/_rounds/_restLast/_cdWindow 对应）。</summary>
public sealed class TrainingTemplate
{
    public string Name { get; set; } = "";
    public int Rounds { get; set; }
    public int WorkSec { get; set; }
    public int RestSec { get; set; }
    public bool RestLast { get; set; }
    public int CdWindow { get; set; }
}
