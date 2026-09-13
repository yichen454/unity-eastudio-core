using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [ExecuteAlways]
    [AddComponentMenu("EAStudio/Sky/Time Of Day Controller")]
    public class TimeOfDay : MonoBehaviour
    {
        public static TimeOfDay Instance { get; private set; }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        [Header("时间流逝与昼夜 (Time & Progression)")]
        [Tooltip("当前时间（24小时制：0 = 午夜，6 = 清晨黎明，12 = 正午，18 = 黄昏日落）。")]
        [Range(0f, 24f)]
        public float timeOfDay = 12f;

        [Tooltip("在运行模式 (Play mode) 下是否自动推进时间流逝。")]
        public bool autoAdvance = false;

        [Tooltip("一个完整 24 小时昼夜循环对应的现实世界秒数。")]
        [Min(1f)]
        public float dayLengthInSeconds = 120f;

        [Header("天文轨道参数 (Astronomical Orbit)")]
        [Tooltip("地理纬度（-90 = 南极，0 = 赤道，90 = 北极），控制太阳升降轨道的倾角轨迹。")]
        [Range(-90f, 90f)]
        public float latitude = 35f;

        [Tooltip("正北罗盘方向旋转偏移（0-360度）。")]
        [Range(0f, 360f)]
        public float northDirection = 0f;

        [Tooltip("太阳赤纬角（-23.5 至 +23.5度），模拟四季变换（冬至至夏至）。")]
        [Range(-23.5f, 23.5f)]
        public float seasonDeclination = 0f;

        [Header("天体光源关联 (Celestial Lights)")]
        [Tooltip("场景中代表太阳的平行光 (Directional Light)。")]
        public Light sunLight;

        [Tooltip("场景中代表月亮的平行光 (Directional Light)。")]
        public Light moonLight;

        [Header("日光参数 (Sun Light Settings)")]
        [Tooltip("正午时太阳直射光的最大光照强度。")]
        [Min(0f)]
        public float sunMaxIntensity = 1.2f;

        [Tooltip("日光全天颜色变化渐变色谱（黎明 -> 日出 -> 正午 -> 日落 -> 暮色）。")]
        public Gradient sunColorGradient;

        [Header("月光参数 (Moon Light Settings)")]
        [Tooltip("子夜时月光直射的最大光照强度。")]
        [Min(0f)]
        public float moonMaxIntensity = 0.25f;

        [Tooltip("月光着色基调。")]
        public Color moonColor = new Color(0.78f, 0.86f, 1.0f);

        [Header("阴影管线管理 (Shadow Management)")]
        [Tooltip("自动管理太阳与月亮的阴影开启状态（仅当天体在地平线上且有光照时开启阴影，节省 GPU 阴影贴图开销）。")]
        public bool manageShadows = true;

        [Tooltip("活跃天体光源所使用的阴影类型。")]
        public LightShadows celestialShadows = LightShadows.Soft;

        private void Reset()
        {
            InitDefaultGradients();
            AutoFindLights();
        }

        private void Awake()
        {
            if (sunColorGradient == null || sunColorGradient.colorKeys.Length == 0)
            {
                InitDefaultGradients();
            }
            AutoFindLights();
        }

        private void InitDefaultGradients()
        {
            sunColorGradient = new Gradient();

            var colorKeys = new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.35f, 0.15f), 0.23f), // 05:30 Dawn
                new GradientColorKey(new Color(1.0f, 0.85f, 0.65f), 0.28f), // 06:45 Sunrise
                new GradientColorKey(new Color(1.0f, 0.98f, 0.95f), 0.50f), // 12:00 Noon
                new GradientColorKey(new Color(1.0f, 0.75f, 0.45f), 0.72f), // 17:15 Sunset golden
                new GradientColorKey(new Color(1.0f, 0.25f, 0.08f), 0.78f)  // 18:45 Dusk red
            };

            var alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            };

            sunColorGradient.SetKeys(colorKeys, alphaKeys);
        }

        public void AutoFindLights()
        {
            if (sunLight == null)
            {
                sunLight = SkyEnvironmentSync.FindSunLight();
            }

            if (moonLight == null)
            {
                moonLight = SkyEnvironmentSync.FindMoonLight();
            }

            if (moonLight == sunLight)
            {
                moonLight = null;
            }
        }

        private void Update()
        {
            if (Application.isPlaying && autoAdvance && dayLengthInSeconds > 0.001f)
            {
                float deltaHours = (Time.deltaTime / dayLengthInSeconds) * 24f;
                timeOfDay = Mathf.Repeat(timeOfDay + deltaHours, 24f);
            }

            ApplyCelestialCycle();
        }

        private void OnValidate()
        {
            ApplyCelestialCycle();
        }

        public void ApplyCelestialCycle()
        {
            AutoFindLights();

            if (moonLight == sunLight)
            {
                moonLight = null;
            }

            // Calculate celestial vectors based on latitude, season, and time of day
            Vector3 sunDir = CalculateSunDirection(timeOfDay, latitude, seasonDeclination, northDirection);
            // Moon orbit opposite sun with natural lunar orbital inclination
            Vector3 moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));

            float timeFraction = Mathf.Clamp01(timeOfDay / 24f);

            // 1. Update Sun Light
            if (sunLight != null)
            {
                sunLight.transform.forward = -sunDir;

                // Elevation fade: 1.0 above horizon, fades smoothly to 0.0 as it sinks below horizon
                float sunElevationFade = Mathf.Clamp01((sunDir.y + 0.08f) / 0.15f);
                sunLight.intensity = sunMaxIntensity * sunElevationFade;
                sunLight.color = (sunColorGradient != null && sunColorGradient.colorKeys.Length > 0)
                    ? sunColorGradient.Evaluate(timeFraction)
                    : Color.white;

                if (manageShadows)
                {
                    sunLight.shadows = (sunElevationFade > 0.05f) ? celestialShadows : LightShadows.None;
                }
            }

            // 2. Update Moon Light
            if (moonLight != null)
            {
                moonLight.transform.forward = -moonDir;

                // Moon elevation fade: only lights scene when moon is up AND sun is down
                float moonElevationFade = Mathf.Clamp01((moonDir.y + 0.05f) / 0.15f);
                float sunNightFade = Mathf.Clamp01((-sunDir.y + 0.05f) / 0.15f);
                float totalMoonFade = moonElevationFade * sunNightFade;

                moonLight.intensity = moonMaxIntensity * totalMoonFade;
                moonLight.color = moonColor;

                if (manageShadows)
                {
                    moonLight.shadows = (totalMoonFade > 0.05f) ? celestialShadows : LightShadows.None;
                }
            }
        }

        public static Vector3 CalculateSunDirection(float hours, float latDeg, float declinationDeg, float northOffsetDeg)
        {
            float hourAngleDeg = (hours - 12f) * 15f;
            float radLat = latDeg * Mathf.Deg2Rad;
            float radDec = declinationDeg * Mathf.Deg2Rad;
            float radH = hourAngleDeg * Mathf.Deg2Rad;

            // Solar elevation (altitude)
            float sinAlt = Mathf.Sin(radLat) * Mathf.Sin(radDec) + Mathf.Cos(radLat) * Mathf.Cos(radDec) * Mathf.Cos(radH);
            float alt = Mathf.Asin(Mathf.Clamp(sinAlt, -1f, 1f));

            // Solar azimuth
            float cosAz = (Mathf.Sin(radDec) - Mathf.Sin(radLat) * sinAlt) / (Mathf.Cos(radLat) * Mathf.Cos(alt) + 1e-5f);
            float az = Mathf.Acos(Mathf.Clamp(cosAz, -1f, 1f));
            if (Mathf.Sin(radH) > 0f)
                az = 2f * Mathf.PI - az;

            az += northOffsetDeg * Mathf.Deg2Rad;

            return new Vector3(
                Mathf.Cos(alt) * Mathf.Sin(az),
                Mathf.Sin(alt),
                Mathf.Cos(alt) * Mathf.Cos(az)
            ).normalized;
        }
    }
}
