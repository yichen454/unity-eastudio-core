using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public enum CelestialLightingMode
    {
        [Tooltip("单灯系统（推荐）：全局仅使用一盏主方向光。白昼跟踪太阳，夜晚复用跟踪月亮，彻底规避移动端/XR管线双光源导致的冗余阴影与DrawCall开销。")]
        Single = 0,

        [Tooltip("双灯系统：同时维护太阳光与月光两盏独立方向光，日夜交接阶段可呈现柔和的日月同辉。")]
        Dual = 1
    }

    public enum CelestialOrbitMode
    {
        [Tooltip("物理天文轨道：基于地理纬度、正北罗盘偏角与黄赤交角四季解算。")]
        Realistic = 0,

        [Tooltip("美术自定义轨道：直观通过仰角与偏航轴旋转摆放太阳与月亮。")]
        Custom = 1
    }

    [ExecuteAlways]
    [AddComponentMenu("EAStudio/Sky/Time Of Day Controller")]
    public class TimeOfDay : MonoBehaviour
    {
        public static TimeOfDay Instance { get; private set; }

        private static readonly int s_CelestialStarsMatrixID = Shader.PropertyToID("_CelestialStarsMatrix");
        private static readonly int s_UseCelestialStarsMatrixID = Shader.PropertyToID("_UseCelestialStarsMatrix");

        [Header("光照模式 (Lighting Mode)")]
        [Tooltip("Single（默认）：单灯复用系统，白昼对齐太阳，夜晚对齐月亮。\nDual：双灯独立系统，分别控制太阳与月光。")]
        public CelestialLightingMode lightingMode = CelestialLightingMode.Single;

        [Header("时间流逝与昼夜 (Time & Progression)")]
        [Tooltip("当前时间（24小时制：0 = 午夜，6 = 清晨黎明，12 = 正午，18 = 黄昏日落）。")]
        [Range(0f, 24f)]
        public float timeOfDay = 12f;

        [Tooltip("在运行模式 (Play mode) 下是否自动推进时间流逝。")]
        public bool autoAdvance = false;

        [Tooltip("一个完整 24 小时昼夜循环对应的现实世界基准秒数。")]
        [Min(1f)]
        public float dayLengthInSeconds = 120f;

        [Tooltip("白昼时间流速倍率（数值越小白天越慢/更长，数值越大白天过得越快）。")]
        [Range(0.1f, 10f)]
        public float dayLengthModifier = 1.0f;

        [Tooltip("黑夜时间流速倍率（数值越小黑夜越慢/更长，数值越大黑夜过得越快）。")]
        [Range(0.1f, 10f)]
        public float nightLengthModifier = 1.0f;

        [Header("天体轨道模式 (Orbit Mode)")]
        [Tooltip("天体运行轨道计算模式：Realistic（真实天文学解算）或 Custom（美术自由定制）。")]
        public CelestialOrbitMode orbitMode = CelestialOrbitMode.Realistic;

        [Header("天文轨道参数 (Realistic Orbit)")]
        [Tooltip("地理纬度（-90 = 南极，0 = 赤道，90 = 北极），控制太阳升降轨道的倾角轨迹。")]
        [Range(-90f, 90f)]
        public float latitude = 35f;

        [Tooltip("正北罗盘方向旋转偏移（0-360度）。")]
        [Range(0f, 360f)]
        public float northDirection = 0f;

        [Tooltip("太阳赤纬角（-23.5 至 +23.5度），模拟四季变换（冬至至夏至）。")]
        [Range(-23.5f, 23.5f)]
        public float seasonDeclination = 0f;

        [Header("美术自定义轨道参数 (Custom Orbit)")]
        [Tooltip("自定义太阳在 X 轴上的时间角度偏移（默认 -6 小时使 12:00 对应天顶 90°）。")]
        public float customSunOffset = -6f;

        [Tooltip("自定义太阳罗盘朝向偏角 (0-360度)。")]
        [Range(0f, 360f)]
        public float customSunRotation = 0f;

        [Tooltip("自定义月亮相对太阳的方位角偏角（默认 180 度即与太阳对冲升降）。")]
        [Range(0f, 360f)]
        public float customMoonRotationOffset = 180f;

        [Header("天体光源关联 (Celestial Lights)")]
        [Tooltip("场景中代表太阳的平行光 (Directional Light)。在单灯模式下此光源将在夜晚同时复用为月光。")]
        public Light sunLight;

        [Tooltip("场景中代表月亮的平行光 (Directional Light)。仅在双灯模式下独立生效；单灯模式下自动静默。")]
        public Light moonLight;

        [Header("日光参数 (Sun Light Settings)")]
        [Tooltip("正午时太阳直射光的最大光照强度。")]
        [Min(0f)]
        public float sunMaxIntensity = 1.2f;

        [Tooltip("基于天体高度/时间的日光强度曲线（默认平滑衰减至地平线以下）。")]
        public AnimationCurve sunIntensityCurve;

        [Tooltip("日光全天颜色变化渐变色谱（黎明 -> 日出 -> 正午 -> 日落 -> 暮色）。")]
        public Gradient sunColorGradient;

        [Header("月光参数 (Moon Light Settings)")]
        [Tooltip("子夜时月光直射的最大光照强度。")]
        [Min(0f)]
        public float moonMaxIntensity = 0.25f;

        [Tooltip("基于天体高度/时间的月光强度曲线。")]
        public AnimationCurve moonIntensityCurve;

        [Tooltip("月光着色基调。")]
        public Color moonColor = new Color(0.78f, 0.86f, 1.0f);

        [Tooltip("可选月光渐变色谱（如果配置了渐变，则优先使用渐变，否则使用单色 moonColor）。")]
        public Gradient moonColorGradient;

        [Header("阴影管线管理 (Shadow Management)")]
        [Tooltip("自动管理阴影开启状态（仅当天体在地平线上且有光照时开启阴影，节省 GPU 阴影贴图开销）。")]
        public bool manageShadows = true;

        [Tooltip("活跃天体光源所使用的阴影类型。")]
        public LightShadows celestialShadows = LightShadows.Soft;

        [Header("星空与天球自转 (Celestial Sky Rotation)")]
        [Tooltip("将天球旋转矩阵传递给全局 Shader (_CelestialStarsMatrix)，驱动星空天体与夜空 HDRI 沿轴自转。")]
        public bool rotateNightSky = true;

        // Decoupled runtime metrics & states
        public float SolarTime { get; private set; } = 0.5f;
        public float LunarTime { get; private set; } = 0.5f;
        public bool IsNight { get; private set; } = false;
        public Vector3 CurrentSunDirection { get; private set; } = Vector3.up;
        public Vector3 CurrentMoonDirection { get; private set; } = -Vector3.up;
        public Color CurrentSunColor { get; private set; } = Color.white;
        public float CurrentSunIntensity { get; private set; } = 1.2f;
        public Color CurrentSunRadiance => CurrentSunColor * CurrentSunIntensity;

        public Color CurrentMoonColor { get; private set; } = new Color(0.78f, 0.86f, 1.0f);
        public float CurrentMoonIntensity { get; private set; } = 0.25f;
        public Color CurrentMoonRadiance => CurrentMoonColor * CurrentMoonIntensity;

        // Lifecycle & transition events
        public event Action<int> OnHourPassed;
        public event Action<int> OnDayPassed;
        public event Action<bool> OnDayNightTransition;

        private int m_LastEvaluatedHour = -1;
        private bool m_LastIsNight = false;
        private float m_DayCycleAccumulator = 0f;

        public int Hour => Mathf.FloorToInt(timeOfDay);
        public int Minute => Mathf.FloorToInt((timeOfDay - Hour) * 60f);
        public int Second => Mathf.FloorToInt((((timeOfDay - Hour) * 60f) - Minute) * 60f);
        public float NormalizedTime => Mathf.Clamp01(timeOfDay / 24f);

        private void OnEnable()
        {
            Instance = this;
            m_LastEvaluatedHour = Hour;
            m_LastIsNight = IsNight;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;

            Shader.SetGlobalFloat(s_UseCelestialStarsMatrixID, 0f);
        }

        private void Reset()
        {
            InitDefaultGradients();
            InitDefaultCurves();
            AutoFindLights();
        }

        private void Awake()
        {
            if (sunColorGradient == null || sunColorGradient.colorKeys.Length == 0)
            {
                InitDefaultGradients();
            }

            if (sunIntensityCurve == null || sunIntensityCurve.keys.Length == 0 ||
                moonIntensityCurve == null || moonIntensityCurve.keys.Length == 0)
            {
                InitDefaultCurves();
            }

            AutoFindLights();
            m_LastEvaluatedHour = Hour;
            m_LastIsNight = IsNight;
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

            moonColorGradient = new Gradient();
            var moonKeys = new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.6f, 0.75f, 0.95f), 0.0f),
                new GradientColorKey(new Color(0.78f, 0.86f, 1.0f), 0.5f),
                new GradientColorKey(new Color(0.6f, 0.75f, 0.95f), 1.0f)
            };
            moonColorGradient.SetKeys(moonKeys, alphaKeys);
        }

        private void InitDefaultCurves()
        {
            sunIntensityCurve = new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 0f, 0f),
                new Keyframe(0.1f, 0.05f, 1f, 1f),
                new Keyframe(0.5f, 1.0f, 0f, 0f),
                new Keyframe(0.9f, 0.05f, -1f, -1f),
                new Keyframe(1.0f, 0.0f, 0f, 0f)
            );

            moonIntensityCurve = new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 0f, 0f),
                new Keyframe(0.15f, 0.1f, 1f, 1f),
                new Keyframe(0.5f, 1.0f, 0f, 0f),
                new Keyframe(0.85f, 0.1f, -1f, -1f),
                new Keyframe(1.0f, 0.0f, 0f, 0f)
            );
        }

        public void AutoFindLights()
        {
            if (sunLight == null)
            {
                sunLight = SkyEnvironmentSync.FindSunLight();
            }

            if (moonLight == null && lightingMode == CelestialLightingMode.Dual)
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
                float speedMod = IsNight ? nightLengthModifier : dayLengthModifier;
                speedMod = Mathf.Max(0.01f, speedMod);

                float deltaHours = (Time.deltaTime / (dayLengthInSeconds * speedMod)) * 24f;
                float previousTime = timeOfDay;
                timeOfDay = Mathf.Repeat(timeOfDay + deltaHours, 24f);

                // Day rollover tracking
                if (timeOfDay < previousTime)
                {
                    m_DayCycleAccumulator += 1f;
                    OnDayPassed?.Invoke(Mathf.FloorToInt(m_DayCycleAccumulator));
                }

                // Hour change tracking
                int currentHour = Hour;
                if (currentHour != m_LastEvaluatedHour)
                {
                    m_LastEvaluatedHour = currentHour;
                    OnHourPassed?.Invoke(currentHour);
                }
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

            // 1. Calculate Celestial Directions based on Orbit Mode
            Vector3 sunDir;
            Vector3 moonDir;

            if (orbitMode == CelestialOrbitMode.Realistic)
            {
                sunDir = CalculateSunDirection(timeOfDay, latitude, seasonDeclination, northDirection);
                moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
            }
            else
            {
                // Custom Artistic Mode: explicit pitch from time and compass yaw
                float sunPitch = (timeOfDay + customSunOffset) * 15f;
                Quaternion sunRot = Quaternion.Euler(sunPitch, customSunRotation, 0f);
                sunDir = (sunRot * Vector3.forward).normalized;

                float moonPitch = sunPitch - 180f;
                Quaternion moonRot = Quaternion.Euler(moonPitch, customSunRotation + customMoonRotationOffset, 0f);
                moonDir = (moonRot * Vector3.forward).normalized;
            }

            CurrentSunDirection = sunDir;
            CurrentMoonDirection = moonDir;

            // 2. Compute Decoupled Solar and Lunar Normalized Metrics (0.0 to 1.0, 0.5 is zenith peak)
            // Remap elevation angle sin(alt) into 0~1 normalized time metrics
            float sunElev = Mathf.Clamp(sunDir.y, -1f, 1f);
            float moonElev = Mathf.Clamp(moonDir.y, -1f, 1f);

            SolarTime = Mathf.Clamp01(sunElev * 0.5f + 0.5f);
            LunarTime = Mathf.Clamp01(moonElev * 0.5f + 0.5f);

            bool newIsNight = sunDir.y < -0.01f;
            if (newIsNight != m_LastIsNight)
            {
                m_LastIsNight = newIsNight;
                IsNight = newIsNight;
                OnDayNightTransition?.Invoke(IsNight);
            }
            else
            {
                IsNight = newIsNight;
            }

            // 3. Evaluate Intensities and Colors
            float sunIntensityEval = (sunIntensityCurve != null && sunIntensityCurve.keys.Length > 0)
                ? sunIntensityCurve.Evaluate(SolarTime)
                : Mathf.Clamp01((sunDir.y + 0.08f) / 0.15f);

            float sunCurrentIntensity = sunMaxIntensity * sunIntensityEval;
            bool sunActive = sunCurrentIntensity > 0.0005f && sunDir.y > -0.1f;

            float moonIntensityEval = (moonIntensityCurve != null && moonIntensityCurve.keys.Length > 0)
                ? moonIntensityCurve.Evaluate(LunarTime)
                : Mathf.Clamp01((moonDir.y + 0.05f) / 0.15f);

            float moonNightMask = Mathf.Clamp01((-sunDir.y + 0.05f) / 0.15f);
            float moonCurrentIntensity = moonMaxIntensity * moonIntensityEval * moonNightMask;
            bool moonActive = moonCurrentIntensity > 0.0005f && moonDir.y > -0.05f;

            float timeFraction = Mathf.Clamp01(timeOfDay / 24f);
            Color evaluatedSunColor = (sunColorGradient != null && sunColorGradient.colorKeys.Length > 0)
                ? sunColorGradient.Evaluate(timeFraction)
                : Color.white;

            Color evaluatedMoonColor = (moonColorGradient != null && moonColorGradient.colorKeys.Length > 0)
                ? moonColorGradient.Evaluate(timeFraction)
                : moonColor;

            CurrentSunColor = evaluatedSunColor;
            CurrentSunIntensity = sunActive ? sunCurrentIntensity : 0f;

            CurrentMoonColor = evaluatedMoonColor;
            CurrentMoonIntensity = moonActive ? moonCurrentIntensity : 0f;

            // 4. Lighting Execution based on LightingMode
            if (lightingMode == CelestialLightingMode.Single)
            {
                ApplySingleLighting(sunDir, moonDir, sunCurrentIntensity, moonCurrentIntensity, evaluatedSunColor, evaluatedMoonColor, sunActive, moonActive);
            }
            else
            {
                ApplyDualLighting(sunDir, moonDir, sunCurrentIntensity, moonCurrentIntensity, evaluatedSunColor, evaluatedMoonColor, sunActive, moonActive);
            }

            // 5. Synchronize Celestial Stars Rotation Matrix
            UpdateStarsMatrix();
        }

        private void ApplySingleLighting(
            Vector3 sunDir, Vector3 moonDir,
            float sunCurrentIntensity, float moonCurrentIntensity,
            Color evaluatedSunColor, Color evaluatedMoonColor,
            bool sunActive, bool moonActive)
        {
            // In Single Light mode, sunLight acts as the unified celestial main directional light.
            if (sunLight != null)
            {
                if (!IsNight)
                {
                    // Day phase: track Sun
                    sunLight.transform.forward = -sunDir;
                    sunLight.intensity = sunCurrentIntensity;
                    sunLight.color = evaluatedSunColor;
                    sunLight.enabled = sunActive;

                    if (manageShadows)
                    {
                        sunLight.shadows = sunActive ? celestialShadows : LightShadows.None;
                    }
                }
                else
                {
                    // Night phase: seamlessly redirect unified light to track Moon
                    sunLight.transform.forward = -moonDir;
                    sunLight.intensity = moonCurrentIntensity;
                    sunLight.color = evaluatedMoonColor;
                    sunLight.enabled = moonActive;

                    if (manageShadows)
                    {
                        sunLight.shadows = moonActive ? celestialShadows : LightShadows.None;
                    }
                }

                if (RenderSettings.sun != sunLight)
                {
                    RenderSettings.sun = sunLight;
                }
            }

            // In single mode, ensure secondary moon light is silenced
            if (moonLight != null && moonLight != sunLight)
            {
                moonLight.enabled = false;
                moonLight.shadows = LightShadows.None;
            }
        }

        private void ApplyDualLighting(
            Vector3 sunDir, Vector3 moonDir,
            float sunCurrentIntensity, float moonCurrentIntensity,
            Color evaluatedSunColor, Color evaluatedMoonColor,
            bool sunActive, bool moonActive)
        {
            bool isSunDominant = (sunCurrentIntensity >= moonCurrentIntensity) || (sunDir.y >= -0.02f);

            // Update Sun Light
            if (sunLight != null)
            {
                sunLight.transform.forward = -sunDir;
                sunLight.intensity = sunCurrentIntensity;
                sunLight.color = evaluatedSunColor;
                sunLight.enabled = sunActive;

                if (manageShadows)
                {
                    sunLight.shadows = (sunActive && isSunDominant) ? celestialShadows : LightShadows.None;
                }
            }

            // Update Moon Light
            if (moonLight != null)
            {
                moonLight.transform.forward = -moonDir;
                moonLight.intensity = moonCurrentIntensity;
                moonLight.color = evaluatedMoonColor;
                moonLight.enabled = moonActive;

                if (manageShadows)
                {
                    moonLight.shadows = (moonActive && !isSunDominant) ? celestialShadows : LightShadows.None;
                }
            }

            if (sunLight != null && RenderSettings.sun != sunLight)
            {
                RenderSettings.sun = sunLight;
            }
        }

        private void UpdateStarsMatrix()
        {
            if (!rotateNightSky)
            {
                Shader.SetGlobalFloat(s_UseCelestialStarsMatrixID, 0f);
                return;
            }

            Quaternion starsRotation;
            if (orbitMode == CelestialOrbitMode.Realistic)
            {
                starsRotation = Quaternion.AngleAxis(90.0f - latitude, Vector3.right) *
                                Quaternion.AngleAxis(northDirection + (timeOfDay / 24f) * 360f, Vector3.up);
            }
            else
            {
                float customStarsPitch = (timeOfDay + customSunOffset) * 15f;
                starsRotation = Quaternion.Euler(customStarsPitch, customSunRotation, 0f);
            }

            Matrix4x4 starsMatrix = Matrix4x4.TRS(Vector3.zero, starsRotation, Vector3.one);
            Shader.SetGlobalMatrix(s_CelestialStarsMatrixID, starsMatrix);
            Shader.SetGlobalFloat(s_UseCelestialStarsMatrixID, 1f);
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

        /// <summary>
        /// Convenience helper to set time of day directly from script or UI slider.
        /// </summary>
        public void SetTimeOfDay(float timeHours)
        {
            timeOfDay = Mathf.Repeat(timeHours, 24f);
            ApplyCelestialCycle();
        }
    }
}
