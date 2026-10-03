using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// RenderGraph pass that generates the downscaled, equirectangular-projected 2D cloud layer.
    /// Owns the generator material, pushes the volume parameter set each frame, and
    /// publishes the resulting RGBA cloud texture as the global <c>_CloudTexture</c>.
    /// </summary>
    public class CloudRenderPass
    {
        private const string k_GeneratorShader = "Hidden/EAStudio/CloudGenerator";
        private const string k_DefaultCloudMapPath = "EAStudio/Sky/DefaultCloudMap";

        /// <summary>
        /// Depth of the cloud shell. The layer is a spherical slab, not a card, and this is its radial
        /// thickness; HDRP derives it the same way (<c>_HighestAltitude = _LowestAltitude + 800</c>). It is
        /// what the shader intersects to get the per-ray chord, so changing it trades a thin deck for a deep
        /// one the way HDRP's does. `thickness` (Density) is a separate control: it is the extinction at a
        /// texel, not the geometry.
        /// </summary>
        private const float k_CloudDeckThicknessMeters = 800f;

        /// <summary>
        /// Distance corresponding to one full revolution of the cloud panorama. Wind drifts along the
        /// panorama's wrapping longitude axis, so wrapping the scroll into one revolution is invisible.
        /// Also pushed to the shader as <c>_CloudMapTileSize</c> so both sides use the same unit.
        /// </summary>
        private const float k_CloudMapTileSizeMeters = 60000f;

        /// <summary>
        /// Mean Earth radius, used only to place the deck's own horizon. A layer at <c>altitude</c> rides a
        /// sphere of <c>R + altitude</c>, whose horizon dips by <c>acos(R / (R + altitude))</c> below the
        /// observer's. That dip is what keeps the deck lit after local sunset.
        /// </summary>
        private const float k_PlanetRadiusMeters = 6371000f;

        private static readonly int s_CloudTextureID = Shader.PropertyToID("_CloudTexture");

        private static readonly int s_CloudMapID = Shader.PropertyToID("_CloudMap");
        private static readonly int s_CameraInvProjectionID = Shader.PropertyToID("_CloudCameraInvProjection");
        private static readonly int s_CameraToWorldID = Shader.PropertyToID("_CloudCameraToWorld");
        private static readonly int s_WorldToClipID = Shader.PropertyToID("_CloudWorldToClip");
        private static readonly int s_SunDirectionID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_MoonDirectionID = Shader.PropertyToID("_MoonDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_CloudSunColor");
        private static readonly int s_MoonColorID = Shader.PropertyToID("_CloudMoonColor");
        private static readonly int s_TintID = Shader.PropertyToID("_CloudTint");
        private static readonly int s_AmbientProbeDimmerID = Shader.PropertyToID("_CloudAmbientProbeDimmer");
        private static readonly int s_AmbientColorID = Shader.PropertyToID("_CloudAmbientColor");
        private static readonly int s_AltitudeID = Shader.PropertyToID("_CloudAltitude");
        private static readonly int s_HorizonFadeID = Shader.PropertyToID("_CloudHorizonFade");
        private static readonly int s_RotationID = Shader.PropertyToID("_CloudRotation");
        private static readonly int s_ChannelWeightsID = Shader.PropertyToID("_CloudChannelWeights");
        private static readonly int s_OpacityID = Shader.PropertyToID("_CloudOpacity");
        private static readonly int s_ExposureID = Shader.PropertyToID("_CloudExposure");
        private static readonly int s_WindOffsetID = Shader.PropertyToID("_CloudWindOffset");
        private static readonly int s_RaymarchingID = Shader.PropertyToID("_CloudRaymarching");
        private static readonly int s_RaymarchingStepsID = Shader.PropertyToID("_CloudRaymarchingSteps");
        private static readonly int s_RaymarchingDensityID = Shader.PropertyToID("_CloudRaymarchingDensity");
        private static readonly int s_MapTileSizeID = Shader.PropertyToID("_CloudMapTileSize");
        private static readonly int s_UpperHemisphereOnlyID = Shader.PropertyToID("_CloudUpperHemisphereOnly");
        private static readonly int s_SunHorizonCosID = Shader.PropertyToID("_CloudSunHorizonCos");
        private static readonly int s_PlanetRadiusID = Shader.PropertyToID("_CloudPlanetRadius");
        private static readonly int s_DeckThicknessID = Shader.PropertyToID("_CloudDeckThickness");

        private Shader m_GeneratorShader;
        private Material m_GeneratorMaterial;
        private Shader m_GeneratorShaderOverride;
        private Texture2D m_DefaultCloudMap;
        private bool m_DefaultCloudMapSearched;

        private int m_Resolution = 2;
        private readonly LowResPass m_LowResPass;
        private RenderPassEvent m_RenderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

        private float m_WindOffsetMeters;
        private float m_DeckYawRad;
        private int m_LastWindFrame = -1;

        public LowResPass LowRes => m_LowResPass;
        public RenderPassEvent RenderPassEvent => m_RenderPassEvent;
        public int Resolution => m_Resolution;

        public void UpdateRenderPassEvent(RenderPassEvent renderPassEvent)
        {
            m_RenderPassEvent = renderPassEvent;
            if (m_LowResPass != null)
                m_LowResPass.renderPassEvent = renderPassEvent;
        }

        public CloudRenderPass(Shader generatorShader = null, RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingSkybox)
        {
            m_GeneratorShaderOverride = generatorShader;
            m_RenderPassEvent = renderPassEvent;
            m_LowResPass = new LowResPass(this);
        }

        public void SetShaderOverride(Shader generatorShader)
        {
            if (m_GeneratorShaderOverride != generatorShader)
            {
                m_GeneratorShaderOverride = generatorShader;
                CoreUtils.Destroy(m_GeneratorMaterial);
                m_GeneratorMaterial = null;
                m_GeneratorShader = null;
            }
        }

        private bool EnsureMaterial()
        {
            if (m_GeneratorMaterial != null)
                return true;

            m_GeneratorShader = m_GeneratorShaderOverride != null
                ? m_GeneratorShaderOverride
                : Shader.Find(k_GeneratorShader);

            if (m_GeneratorShader == null)
            {
                Debug.LogError($"[CloudRenderPass] Cannot find shader '{k_GeneratorShader}'.");
                return false;
            }

            m_GeneratorMaterial = CoreUtils.CreateEngineMaterial(m_GeneratorShader);
            return m_GeneratorMaterial != null;
        }

        /// <summary>
        /// Pushes the cloud layer volume state onto the generator material and advances the wind offset.
        /// Called once per frame by <see cref="SkyRenderFeature"/> before the low-res pass is enqueued.
        /// </summary>
        public void Setup(VisualEnvironment visualEnv, CloudLayer cloudLayer, ProceduralSky proceduralSky = null)
        {
            if (!EnsureMaterial() || visualEnv == null || cloudLayer == null)
                return;

            m_Resolution = (int)cloudLayer.resolution.value;

            Material mat = m_GeneratorMaterial;

            Texture cloudMap = cloudLayer.cloudMap.value != null ? cloudLayer.cloudMap.value : EnsureDefaultCloudMap();
            mat.SetTexture(s_CloudMapID, cloudMap != null ? cloudMap : Texture2D.whiteTexture);

            TimeOfDay tod = TimeOfDay.Instance;
            Vector3 sunDir = tod != null ? tod.CurrentSunDirection : Vector3.up;
            Vector3 moonDir = tod != null ? tod.CurrentMoonDirection : Vector3.down;
            Color sunColor = tod != null ? tod.CurrentSunRadiance : Color.white;
            Color moonColor = tod != null ? tod.CurrentMoonRadiance : Color.black;

            // The deck sits inside the atmosphere, so its light is sunlight that has already crossed the whole
            // column. Tinting it with the dome's own transmittance is HDRP's `EvaluateSunColorAttenuation` on the
            // 2D cloud layer, and it is where a low sun's clouds get their ember red: blue and green are
            // extinguished before red long before the sun reaches the horizon. The same filter is applied to the
            // fill the deck receives from below, because that light is the ground and the lower atmosphere lit by
            // the very same beam. Either term left unfiltered keeps its midday colour while the sky goes dark,
            // and once the sun is down the fill is all that is left -- a constant that paints every texel of the
            // deck identically. Under an HDRI sky there is no atmosphere to evaluate, so the light passes through
            // untouched, matching HDRP, which only attenuates under PhysicallyBasedSky.
            Vector3 sunTransmittance = proceduralSky != null
                ? ProceduralSkyRadiance.SunTransmittance(sunDir, proceduralSky.atmosphereThickness.value,
                                                         proceduralSky.aerosolHaze.value, proceduralSky.ozoneAbsorption.value)
                : Vector3.one;
            sunColor = new Color(sunColor.r * sunTransmittance.x, sunColor.g * sunTransmittance.y,
                                 sunColor.b * sunTransmittance.z, sunColor.a);

            mat.SetVector(s_SunDirectionID, sunDir);
            mat.SetVector(s_MoonDirectionID, moonDir);
            mat.SetColor(s_SunColorID, sunColor);
            mat.SetColor(s_MoonColorID, moonColor);

            mat.SetColor(s_TintID, cloudLayer.tint.value);
            mat.SetFloat(s_AltitudeID, cloudLayer.altitude.value);
            // Fade is authored in degrees of elevation because that is what an artist can reason about against
            // the skyline; the shader compares it against `rayDir.y`, i.e. sin(elevation), so the conversion
            // happens here. The deck is culled entirely below the horizon, which is in the shader.
            mat.SetFloat(s_HorizonFadeID, Mathf.Sin(Mathf.Clamp(cloudLayer.horizonFade.value, 0f, 90f) * Mathf.Deg2Rad));
            m_DeckYawRad = cloudLayer.rotation.value * Mathf.Deg2Rad;
            mat.SetFloat(s_RotationID, m_DeckYawRad);
            mat.SetFloat(s_OpacityID, cloudLayer.opacity.value);
            mat.SetFloat(s_ExposureID, Mathf.Pow(2f, cloudLayer.exposure.value));
            mat.SetVector(s_ChannelWeightsID, new Vector4(
                cloudLayer.opacityR.value,
                cloudLayer.opacityG.value,
                cloudLayer.opacityB.value,
                cloudLayer.opacityA.value));
            mat.SetFloat(s_RaymarchingID, cloudLayer.lighting.value ? 1f : 0f);
            mat.SetFloat(s_RaymarchingStepsID, cloudLayer.steps.value);
            mat.SetFloat(s_RaymarchingDensityID, cloudLayer.thickness.value);
            mat.SetFloat(s_AmbientProbeDimmerID, cloudLayer.ambientProbeDimmer.value);
            // HDRP samples the ambient probe straight down: a cloud base is lit by whatever is below it, not
            // by the sky it is hiding. The probe here is the one ProceduralSkyController bakes each frame, so
            // the cloud shadow side tracks the sky instead of sitting at a hardcoded grey.
            Color ambientDown = EvaluateAmbientDown();
            mat.SetColor(s_AmbientColorID, new Color(ambientDown.r * sunTransmittance.x, ambientDown.g * sunTransmittance.y,
                                                     ambientDown.b * sunTransmittance.z, ambientDown.a));
            mat.SetFloat(s_MapTileSizeID, k_CloudMapTileSizeMeters);
            mat.SetFloat(s_UpperHemisphereOnlyID, cloudLayer.upperHemisphereOnly.value ? 1f : 0f);
            // Cosine of the deck's horizon dip. Negative, because the deck can see slightly below the
            // observer's horizon; at the default 2000 m altitude it is about -0.025, i.e. the sun keeps
            // lighting the deck until it is 1.4 degrees below the skyline.
            float deckRadius = k_PlanetRadiusMeters + Mathf.Max(0f, cloudLayer.altitude.value);
            float radiusRatio = k_PlanetRadiusMeters / deckRadius;
            mat.SetFloat(s_SunHorizonCosID, -Mathf.Sqrt(Mathf.Max(0f, 1f - radiusRatio * radiusRatio)));
            // The shell the shader intersects: planet radius plus the radial depth above altitude.
            mat.SetFloat(s_PlanetRadiusID, k_PlanetRadiusMeters);
            mat.SetFloat(s_DeckThicknessID, k_CloudDeckThicknessMeters);
            // Wind scroll: the panorama can only move along its wrapping longitude axis, so the world-space
            // wind vector is projected onto that axis (the meridian that maps to u = 0.5). Only the tangential
            // component is representable; a cross-wind has no way to move an equirectangular layer.
            // Guard by frame so multi-camera setups advance the offset once per frame.
            if (m_LastWindFrame != Time.frameCount)
            {
                m_LastWindFrame = Time.frameCount;
                // Wind "None" holds the deck still; "Horizontal" (Procedural) scrolls it, the only mode the
                // 2D layer supports.
                if (cloudLayer.distortionMode.value == CloudDistortionMode.Procedural)
                {
                    float windAngle = cloudLayer.scrollOrientation.value * Mathf.Deg2Rad;
                    Vector2 windDir = new Vector2(Mathf.Cos(windAngle), Mathf.Sin(windAngle));
                    Vector2 longitudeAxis = new Vector2(Mathf.Sin(m_DeckYawRad), Mathf.Cos(m_DeckYawRad));
                    float drift = Vector2.Dot(windDir, longitudeAxis) * cloudLayer.scrollSpeed.value;
                    m_WindOffsetMeters += drift * Time.deltaTime;
                }

                // One revolution of the panorama covers k_CloudMapTileSizeMeters, so wrapping the scroll is
                // invisible. Without it the offset grows without bound and float precision degrades.
                m_WindOffsetMeters = Mathf.Repeat(m_WindOffsetMeters, k_CloudMapTileSizeMeters);
            }

            mat.SetVector(s_WindOffsetID, new Vector4(m_WindOffsetMeters, 0f, 0f, 0f));
        }

        // Reused so the per-frame ambient evaluation does not allocate.
        private static readonly Vector3[] s_AmbientDownDirections = { Vector3.down };
        private static readonly Color[] s_AmbientDownResults = new Color[1];

        /// <summary>
        /// Evaluates the baked sky ambient probe in the down direction, matching HDRP's cloud ambient term.
        /// </summary>
        private static Color EvaluateAmbientDown()
        {
            RenderSettings.ambientProbe.Evaluate(s_AmbientDownDirections, s_AmbientDownResults);
            return s_AmbientDownResults[0];
        }

        private Texture2D EnsureDefaultCloudMap()
        {
            if (!m_DefaultCloudMapSearched)
            {
                m_DefaultCloudMapSearched = true;
                m_DefaultCloudMap = Resources.Load<Texture2D>(k_DefaultCloudMapPath);
            }
            return m_DefaultCloudMap;
        }

        public class LowResPass : ScriptableRenderPass
        {
            private readonly CloudRenderPass m_Parent;
            private readonly Matrix4x4[] m_InvProjections = new Matrix4x4[2];
            private readonly Matrix4x4[] m_CameraToWorld = new Matrix4x4[2];
            private readonly Matrix4x4[] m_WorldToClip = new Matrix4x4[2];

            public LowResPass(CloudRenderPass parent)
            {
                m_Parent = parent;
                renderPassEvent = m_Parent.m_RenderPassEvent;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle cloudTexture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Parent.m_GeneratorMaterial == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

                // World-space view rays: the generator reconstructs them from the low-res uv.
                Camera camera = cameraData.camera;
                if (camera != null)
                {
                    Material mat = m_Parent.m_GeneratorMaterial;
                    int viewCount = 1;
#if ENABLE_VR && ENABLE_XR_MODULE
                    if (cameraData.xr.enabled && cameraData.xr.singlePassEnabled)
                        viewCount = cameraData.xr.viewCount;
#endif
                    for (int view = 0; view < 2; view++)
                    {
                        int viewIndex = Mathf.Min(view, viewCount - 1);
                        m_InvProjections[view] = cameraData.GetProjectionMatrix(viewIndex).inverse;
                        m_CameraToWorld[view] = cameraData.GetViewMatrix(viewIndex).inverse;
                        m_WorldToClip[view] = cameraData.GetProjectionMatrix(viewIndex) * cameraData.GetViewMatrix(viewIndex);
                    }
                    mat.SetMatrixArray(s_CameraInvProjectionID, m_InvProjections);
                    mat.SetMatrixArray(s_CameraToWorldID, m_CameraToWorld);
                    if (RenderSettings.skybox != null)
                        RenderSettings.skybox.SetMatrixArray(s_WorldToClipID, m_WorldToClip);
                }

                int divisor = m_Parent.m_Resolution;
                if (divisor != 1 && divisor != 2 && divisor != 4 && divisor != 8)
                    divisor = 2;
                desc.width = Mathf.Max(1, (desc.width + divisor - 1) / divisor);
                desc.height = Mathf.Max(1, (desc.height + divisor - 1) / divisor);
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                TextureDesc textureDesc = new TextureDesc(desc.width, desc.height)
                {
                    colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                    dimension = desc.dimension,
                    slices = desc.volumeDepth,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Mirror,
                    name = "_CloudTexture"
                };

                TextureHandle cloudTex = renderGraph.CreateTexture(textureDesc);

                SkyCloudFrameData cloudFrameData = frameData.GetOrCreate<SkyCloudFrameData>();
                cloudFrameData.cloudTexture = cloudTex;
                cloudFrameData.hasClouds = true;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Generate Clouds (LowRes)", out var passData))
                {
                    passData.material = m_Parent.m_GeneratorMaterial;
                    passData.cloudTexture = cloudTex;

                    if (resourceData != null && resourceData.cameraDepthTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(cloudTex, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(cloudTex, s_CloudTextureID);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                    });
                }
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_GeneratorMaterial);
            m_GeneratorMaterial = null;
        }
    }
}
