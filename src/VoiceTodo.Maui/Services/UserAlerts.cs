using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Resources;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 统一的用户提示（存储失败等）。把仓库/调度抛出的异常转成用户可读对话框，
/// 避免 async void 入口静默吞掉异常（见 page-optimization-review §存储异常的页面处理未补齐）。
/// 提示自身失败一律吞掉，绝不因“提示失败”再次抛出。
/// </summary>
public static class UserAlerts
{
    /// <summary>是否为本地存储读写异常（损坏/写失败）。</summary>
    public static bool IsStorage(Exception ex) => ex is StorageException;

    /// <summary>
    /// 若为存储异常则提示用户并返回 true；其它异常返回 false（由调用方决定是否自行处理）。
    /// </summary>
    public static async Task<bool> ShowIfStorageAsync(Exception ex, string? detail = null)
    {
        if (ex is not StorageException) return false;
        await ShowAsync(AppResources.StorageErrorTitle,
            string.IsNullOrWhiteSpace(detail)
                ? string.Format(AppResources.StorageErrorMessage, ex.Message)
                : string.Format(AppResources.StorageErrorMessage, detail));
        return true;
    }

    /// <summary>
    /// 存储异常快捷提示（异常重载）：用于 async void 入口的兜底 catch。
    /// 无论传入的是否为 StorageException，都按「存储异常 + 原因」展示，避免入口静默吞异常。
    /// </summary>
    public static Task ShowStorageErrorAsync(Exception ex)
        => ShowAsync(AppResources.StorageErrorTitle,
            string.Format(AppResources.StorageErrorMessage, ex.Message));

    /// <summary>存储异常快捷提示（明细重载）：调用方已拿到可读原因文本时使用。</summary>
    public static Task ShowStorageErrorAsync(string detail)
        => ShowAsync(AppResources.StorageErrorTitle,
            string.Format(AppResources.StorageErrorMessage, detail));

    /// <summary>确认式重试对话框：用于加载失败等可重试场景；取消返回 false。</summary>
    public static async Task<bool> ConfirmRetryAsync(string title, string message)
    {
        try
        {
            var page = Shell.Current;
            if (page is null) return false;
            return await page.DisplayAlert(title, message, AppResources.Retry, AppResources.Cancel);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>普通提示对话框（确定）。</summary>
    public static async Task ShowAsync(string title, string message)
    {
        try
        {
            var page = Shell.Current;
            if (page is null) return;
            await page.DisplayAlert(title, message, AppResources.OK);
        }
        catch
        {
            // 提示失败不影响宿主。
        }
    }
}
