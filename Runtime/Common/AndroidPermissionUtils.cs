using UnityEngine;

public static class AndroidPermissionUtils
{
    /// <summary>
    /// 检查是否已拥有“所有文件访问权限”（Android 11 / API 30+）
    /// </summary>
    public static bool HasAllFilesAccess()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Android 11 (API level 30) 及以上才需要/支持此权限
        using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            int sdkInt = buildVersion.GetStatic<int>("SDK_INT");
            if (sdkInt < 30)
            {
                // API < 30 (Android 10及以下) 走常规的读写权限流程，默认视为已允许或直接返回 true
                return true; 
            }
        }

        using (var envClass = new AndroidJavaClass("android.os.Environment"))
        {
            return envClass.CallStatic<bool>("isExternalStorageManager");
        }
#else
        return true; // Editor 或非 Android 平台默认返回 true，方便本地调试
#endif
    }

    /// <summary>
    /// 跳转至系统设置页，引导用户开启“所有文件访问权限”
    /// </summary>
    public static void RequestAllFilesAccess()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // 避免重复开启
        if (HasAllFilesAccess())
        {
            Debug.Log("[Permission] 已拥有所有文件访问权限，无需申请。");
            return;
        }

        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.MANAGE_ALL_FILES_ACCESS_PERMISSION"))
        {
            currentActivity.Call("startActivity", intent);
        }
#else
        Debug.Log("[Permission] Editor 环境下模拟跳转申请。");
#endif
    }
}