using System.Threading.Channels;
using VoiceTodo.Maui.PlatformServices;
// MAUI 全局 using 引入了 Microsoft.Maui.Media.ITextToSpeech，与 Core 同名类型冲突 → 显式别名到 Core 契约。
using ITextToSpeech = VoiceTodo.Core.Abstractions.ITextToSpeech;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 串行播报队列（DEV-05 末秒倒数与阶段提示的单一音频队列，05 §报数）：
/// - 入队携带「绝对生效截止时刻」，出队时已过期即丢弃（播报永不延长计时）；
/// - 暂停 → Clear 清空待播；恢复 → 不补播（数字倒数由运行循环按真实剩余时间自然续报）；
/// - 每条播报 fire-and-forget，串行消费，绝不阻塞计时推进；
/// - 播报前压低后台音（B6 共存），完成即恢复。
/// </summary>
public sealed class AnnouncerQueue
{
    private sealed class Entry
    {
        public required string Text { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private readonly Channel<Entry> _channel =
        Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ITextToSpeech _tts;
    private readonly IAudioCoexist _audioCoexist;
    private CancellationTokenSource? _speakCts;

    public AnnouncerQueue(ITextToSpeech tts, IAudioCoexist audioCoexist)
    {
        _tts = tts;
        _audioCoexist = audioCoexist;
        _ = ConsumeAsync(); // 应用生命周期后台消费
    }

    /// <summary>入队一条播报；expiresAt 之前未轮到播出即作废。</summary>
    public void Enqueue(string text, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _channel.Writer.TryWrite(new Entry { Text = text, ExpiresAt = expiresAt });
    }

    /// <summary>清空待播并中断正在播的语句（暂停/结束训练时调用）。恢复不补播。</summary>
    public void Clear()
    {
        while (_channel.Reader.TryRead(out _)) { }
        try { _speakCts?.Cancel(); } catch { /* 忽略 */ }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync())
        {
            if (entry.ExpiresAt < DateTimeOffset.Now)
                continue; // 过期提示丢弃，不能播完一长句才切阶段
            try
            {
                using var cts = _speakCts = new CancellationTokenSource();
                bool duck = AppSettings.AudioCoexist;
                if (duck) _audioCoexist.DuckForSpeech();
                try
                {
                    await _tts.SpeakAsync(entry.Text, null, cts.Token);
                }
                finally
                {
                    if (duck) _audioCoexist.Restore();
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                // 单条播报失败不拖垮队列，继续消费后续条目
            }
            finally
            {
                _speakCts = null;
            }
        }
    }
}
