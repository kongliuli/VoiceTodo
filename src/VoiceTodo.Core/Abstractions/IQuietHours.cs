namespace VoiceTodo.Core.Abstractions;

/// <summary>
/// 静默时段（免打扰）配置抽象：让调度器在不依赖平台的前提下得知免打扰开关与起止时刻。
/// 实现由 Maui 侧提供（读 Preferences），Core 仅消费，便于单元测试与跨平台复用。
/// </summary>
public interface IQuietHours
{
    /// <summary>是否启用静默时段。</summary>
    bool Enabled { get; }

    /// <summary>静默开始时刻（一天内的时刻，跨午夜由 End &lt; Start 表示）。</summary>
    TimeSpan Start { get; }

    /// <summary>静默结束时刻（一天内的时刻，End &lt; Start 表示跨午夜）。</summary>
    TimeSpan End { get; }
}
