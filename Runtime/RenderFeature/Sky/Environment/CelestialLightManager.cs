using UnityEngine;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Coordinates discovery, caching, and astronomical separation of Sun and Moon directional lights.
    /// </summary>
    public static class CelestialLightManager
    {
        private static Light s_CachedSunLight;
        private static Light s_CachedMoonLight;
        private static int s_LastLightScanFrame = -1000;

        public static Light FindSunLight()
        {
            RefreshLightCacheIfNeeded();
            return s_CachedSunLight;
        }

        public static Light FindMoonLight()
        {
            RefreshLightCacheIfNeeded();
            return s_CachedMoonLight;
        }

        public static void EnsureSeparated(Vector3 sunDir, ref Vector3 moonDir)
        {
            if (Vector3.Dot(sunDir, moonDir) > 0.95f)
            {
                moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
            }
        }

        public static void Reset()
        {
            s_CachedSunLight = null;
            s_CachedMoonLight = null;
            s_LastLightScanFrame = -1000;
        }

        private static void RefreshLightCacheIfNeeded()
        {
            int currentFrame = Time.frameCount;
            if (s_CachedSunLight != null &&
                (s_CachedMoonLight == null || s_CachedMoonLight != s_CachedSunLight) &&
                Mathf.Abs(currentFrame - s_LastLightScanFrame) < 60)
            {
                return;
            }

            s_LastLightScanFrame = currentFrame;
            s_CachedSunLight = null;
            s_CachedMoonLight = null;

            // 1. If TimeOfDay controller is present, it explicitly owns celestial light bindings
            // NEVER check isActiveAndEnabled to identify the celestial body: Sun is Sun even when sleeping at night!
            if (TimeOfDay.Instance != null)
            {
                if (TimeOfDay.Instance.sunLight != null)
                {
                    s_CachedSunLight = TimeOfDay.Instance.sunLight;
                }
                if (TimeOfDay.Instance.moonLight != null && TimeOfDay.Instance.moonLight != s_CachedSunLight)
                {
                    s_CachedMoonLight = TimeOfDay.Instance.moonLight;
                }
                return;
            }

            // 2. Scan scene lights by identity and name (exclude Moon from ever becoming Sun)
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Light sunFallback = null;
            Light moonFallback = null;

            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null || l.type != LightType.Directional)
                    continue;

                string name = l.name.ToLowerInvariant();
                if (s_CachedSunLight == null)
                {
                    if (name.Contains("sun"))
                        s_CachedSunLight = l;
                    else if (!name.Contains("moon") && sunFallback == null)
                        sunFallback = l;
                }

                if (s_CachedMoonLight == null)
                {
                    if (name.Contains("moon"))
                        s_CachedMoonLight = l;
                    else if (!name.Contains("sun") && moonFallback == null)
                        moonFallback = l;
                }
            }

            if (s_CachedSunLight == null)
                s_CachedSunLight = sunFallback;

            if (s_CachedMoonLight == null && moonFallback != s_CachedSunLight)
                s_CachedMoonLight = moonFallback;
        }
    }
}
