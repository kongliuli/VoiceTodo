using Microsoft.Maui.ApplicationModel;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 通知权限申请 helper（A1）：Android 13+ 需要运行时申请 POST_NOTIFICATIONS 才能弹出提醒；
/// 旧版本 Android 安装即有权限；非 Android 平台无运行时通知权限弹窗，直接视为已授予。
/// 本类绝不抛异常——任何异常（含用户拒绝、系统未提供入口）都当作「未授予」返回 false，
/// 调用方据此决定提示文案，但绝不应因返回 false 而阻断保存流程（不谎报原则）。
/// </summary>
public static class NotificationPermission
{
    /// <summary>
    /// 确保通知权限已授予：未授予时弹系统申请框；已授予/无需申请直接返回 true。
    /// Android 13+ 用 <see cref="Permissions.CheckStatusAsync{T}"/> / <see cref="Permissions.RequestAsync{T}"/>，
    /// 并以 <see cref="OperatingSystem.IsAndroidVersionAtLeast(int)"/> 守卫（12 及以下无需申请）。
    /// 非 Android 直接返回 true；任何异常返回 false。
    /// </summary>
    public static async Task<bool> EnsureAsync()
    {
        try
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                return true; // Android 12 及以下：安装即授予，无需运行时申请
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status == PermissionStatus.Granted)
                return true;
            status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            return status == PermissionStatus.Granted;
#else
            return true; // 非 Android 平台无运行时通知权限弹窗
#endif
        }
        catch
        {
            return false; // 申请过程异常 → 如实视为未授予，不谎报
        }
    }

    /// <summary>仅查询当前授权状态（不弹窗），用于保存后决定提示文案。</summary>
    public static async Task<bool> IsGrantedAsync()
    {
        try
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                return true;
            return await Permissions.CheckStatusAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
#else
            return true;
#endif
        }
        catch
        {
            return false;
        }
    }
}
