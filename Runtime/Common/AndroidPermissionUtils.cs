using UnityEngine;

namespace EAStudio.Core
{
    public static class AndroidPermissionUtils
    {
        /// <summary>
        /// 检查是否拥有所有文件访问权限
        /// Android 11 / API 30+
        /// </summary>
        public static bool HasAllFilesAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                int sdkInt = buildVersion.GetStatic<int>("SDK_INT");

                if (sdkInt < 30)
                    return true;
            }

            using (var envClass = new AndroidJavaClass("android.os.Environment"))
            {
                return envClass.CallStatic<bool>("isExternalStorageManager");
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// 请求所有文件访问权限
        /// </summary>
        public static void RequestAllFilesAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (HasAllFilesAccess())
            {
                Debug.Log("[Permission] 已拥有所有文件访问权限。");
                return;
            }

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject(
                "android.content.Intent",
                "android.settings.MANAGE_ALL_FILES_ACCESS_PERMISSION"))
            {
                currentActivity.Call("startActivity", intent);
            }
#else
            Debug.Log("[Permission] Editor 环境下模拟权限申请。");
#endif
        }
    }
}