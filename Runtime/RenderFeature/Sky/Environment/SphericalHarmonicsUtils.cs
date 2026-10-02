using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Shared sky-to-probe conventions: the real-SH basis, the clamped-cosine convolution that turns radiance into
    /// Unity's <see cref="RenderSettings.ambientProbe"/> scale, and the material-side gamma decode that feeds them.
    /// Every probe producer in this namespace goes through here, because a probe blended from two conventions is
    /// meaningless.
    /// </summary>
    public static class SphericalHarmonicsUtils
    {
        private static readonly Dictionary<int, CubemapProbe> s_CubemapCache = new Dictionary<int, CubemapProbe>();
        private static readonly HashSet<int> s_UnreadableCubemapWarnings = new HashSet<int>();

        /// <summary>
        /// Per-face integration grid used to project a cubemap into a probe. A Fibonacci sphere is plenty for the
        /// smooth procedural dome, but an HDRI carries a handful of very bright texels (sun, lamps, windows) that the
        /// RGBM decode weights up to 30x above the mean, so a uniform random pass aliases badly on L0. A 32x32 lattice
        /// with exact per-texel solid angles is deterministic and reproduces Unity's own Skybox ambient bake to better
        /// than 0.2% at a quarter of the sample count. The result is cached per cubemap.
        /// </summary>
        private const int k_CubemapFaceResolution = 32;

        /// <summary>Real-SH basis constants of Unity's <see cref="SphericalHarmonicsL2"/> layout, in coefficient order.</summary>
        internal static readonly float[] kBasis =
        {
            0.282095f,
            0.488603f, 0.488603f, 0.488603f,
            1.092548f, 1.092548f, 0.315392f, 1.092548f, 0.546274f,
        };

        /// <summary>
        /// Clamped-cosine convolution coefficients <c>A_l / pi</c>, the scaling that turns a raw radiance projection into
        /// the irradiance/pi convention <see cref="RenderSettings.ambientProbe"/> uses. Shared by every probe producer in
        /// this namespace: a probe blended from two different conventions is meaningless.
        /// </summary>
        internal static readonly float[] kCosineLobe = { 1f, 2f / 3f, 2f / 3f, 2f / 3f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f };

        /// <summary>
        /// True when the project renders in linear colour space. The only case where Unity converts <see cref="Color"/>
        /// properties and <c>[Gamma]</c> floats on material upload, and where texture samplers linearise sRGB data.
        /// </summary>
        public static bool LinearColorSpace => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>Mirrors the decode Unity applies to a <see cref="Color"/> property on material upload.</summary>
        public static Vector3 DecodeGamma(Color color)
        {
            return LinearColorSpace
                ? new Vector3(Mathf.GammaToLinearSpace(color.r), Mathf.GammaToLinearSpace(color.g), Mathf.GammaToLinearSpace(color.b))
                : new Vector3(color.r, color.g, color.b);
        }

        /// <summary>Mirrors the decode Unity applies to a <c>[Gamma]</c> float property on material upload.</summary>
        public static float DecodeGamma(float value)
        {
            return LinearColorSpace ? Mathf.GammaToLinearSpace(value) : value;
        }

        /// <summary>
        /// Exact analytical rotation of Unity's SphericalHarmonicsL2 around the Y-axis.
        /// <para>
        /// The probe is treated as the function it reconstructs, <c>f(d) = sum_i sh[i] * P_i(d)</c>, and the result is
        /// <c>f'(d) = f(R_y(-angle) * d)</c>: the sky pattern turns by <c>+angle</c> about +Y, so a feature sitting on +Z
        /// moves towards +X. Callers must pick the sign that matches what their skybox shader samples, and both do:
        /// the HDRI skybox samples along <c>R_y(+_Rotation)</c> and passes <c>-_Rotation</c>, the procedural night map
        /// samples along <c>R_y(-_NightRotation)</c> and passes <c>+_NightRotation</c>.
        /// </para>
        /// <para>
        /// <see cref="SphericalHarmonicsL2"/> stores raw polynomial coefficients: <c>P_i</c> is
        /// <c>{1, y, z, x, xy, yz, 3z^2-1, xz, x^2-y^2}</c> with no basis constant folded in, which is also exactly what
        /// <see cref="SphericalHarmonicsL2.Evaluate"/> and the <c>SampleSH9</c> shader path reconstruct. So the rotation
        /// below is a change of basis on those polynomials, not a rotation of orthonormal SH coefficients.
        /// </para>
        /// </summary>
        public static SphericalHarmonicsL2 RotateY(SphericalHarmonicsL2 sh, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            float c2 = Mathf.Cos(2f * rad);
            float s2 = Mathf.Sin(2f * rad);

            SphericalHarmonicsL2 result = new SphericalHarmonicsL2();

            for (int rgb = 0; rgb < 3; rgb++)
            {
                // L0
                result[rgb, 0] = sh[rgb, 0];

                // L1
                // 1: Y
                result[rgb, 1] = sh[rgb, 1];
                // 2: Z, 3: X
                result[rgb, 2] = -s * sh[rgb, 3] + c * sh[rgb, 2];
                result[rgb, 3] = c * sh[rgb, 3] + s * sh[rgb, 2];

                // L2
                // 4: XY, 5: YZ
                result[rgb, 4] = c * sh[rgb, 4] + s * sh[rgb, 5];
                result[rgb, 5] = -s * sh[rgb, 4] + c * sh[rgb, 5];

                // 6: 3Z^2 - 1, 7: XZ, 8: X^2 - Y^2
                float l6 = sh[rgb, 6];
                float l7 = sh[rgb, 7];
                float l8 = sh[rgb, 8];

                result[rgb, 6] = 0.25f * (1f + 3f * c2) * l6 - 0.25f * s2 * l7 + 0.25f * (1f - c2) * l8;
                result[rgb, 7] = 3f * s2 * l6 + c2 * l7 - s2 * l8;
                result[rgb, 8] = 0.75f * (1f - c2) * l6 + 0.25f * s2 * l7 + 0.25f * (3f + c2) * l8;
            }

            return result;
        }

        /// <summary>Per-channel scaling, e.g. by a skybox tint factor that is not a <see cref="Color"/>.</summary>
        public static SphericalHarmonicsL2 Scale(SphericalHarmonicsL2 sh, Vector3 rgb)
        {
            SphericalHarmonicsL2 result = new SphericalHarmonicsL2();
            for (int i = 0; i < 9; i++)
            {
                result[0, i] = sh[0, i] * rgb.x;
                result[1, i] = sh[1, i] * rgb.y;
                result[2, i] = sh[2, i] * rgb.z;
            }
            return result;
        }

        public static SphericalHarmonicsL2 Scale(SphericalHarmonicsL2 sh, float factor)
        {
            SphericalHarmonicsL2 result = new SphericalHarmonicsL2();
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int i = 0; i < 9; i++)
                {
                    result[rgb, i] = sh[rgb, i] * factor;
                }
            }
            return result;
        }

        public static SphericalHarmonicsL2 Lerp(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b, float t)
        {
            t = Mathf.Clamp01(t);
            float invT = 1f - t;
            SphericalHarmonicsL2 result = new SphericalHarmonicsL2();

            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int i = 0; i < 9; i++)
                {
                    result[rgb, i] = a[rgb, i] * invT + b[rgb, i] * t;
                }
            }

            return result;
        }

        public static SphericalHarmonicsL2 Add(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            SphericalHarmonicsL2 result = new SphericalHarmonicsL2();
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int i = 0; i < 9; i++)
                {
                    result[rgb, i] = a[rgb, i] + b[rgb, i];
                }
            }
            return result;
        }

        public static SphericalHarmonicsL2 FromTrilight(Color sky, Color equator, Color ground)
        {
            SphericalHarmonicsL2 sh = new SphericalHarmonicsL2();

            for (int c = 0; c < 3; c++)
            {
                float s = sky[c];
                float e = equator[c];
                float g = ground[c];

                float avg = (s + 2f * e + g) * 0.25f;
                float diff = (s - g) * 0.5f;

                sh[c, 0] = avg;
                sh[c, 1] = diff * 0.5f;
            }

            return sh;
        }

        /// <summary>
        /// Projects a cubemap (night HDRI or HDRI skybox) into the same ambient probe convention the procedural dome
        /// uses. Non-readable cubemaps cannot be sampled at all; they fall back to a neutral grey trilight and warn
        /// once, because silently returning a probe unrelated to the visible sky is worse than saying so.
        /// </summary>
        /// <param name="cubemap">Cubemap to project. May be null.</param>
        /// <param name="decodeInstructions">
        /// Unity's <c>_Tex_HDR</c> style RGBM decode vector for this cubemap, read back from the skybox material after
        /// its texture was assigned. Pass <c>Vector4.zero</c> for an unencoded cubemap.
        /// </param>
        /// <param name="sh">Projected probe.</param>
        /// <returns>False when <paramref name="cubemap"/> is null.</returns>
        public static bool ExtractFromCubemap(Cubemap cubemap, Vector4 decodeInstructions, out SphericalHarmonicsL2 sh)
        {
            if (cubemap == null)
            {
                sh = default;
                return false;
            }

            int id = cubemap.GetInstanceID();
            if (s_CubemapCache.TryGetValue(id, out var cached) && cached.decodeInstructions == decodeInstructions)
            {
                sh = cached.probe;
                return true;
            }

            if (!cubemap.isReadable && s_UnreadableCubemapWarnings.Add(id))
            {
                Debug.LogWarning(
                    "[EAStudio Sky] Cubemap '" + cubemap.name + "' is not readable, so its ambient probe cannot be " +
                    "derived from its pixels and a neutral fallback is used instead. Enable Read/Write in the " +
                    "texture import settings to light the scene with the sky the player sees.");
            }

            sh = ComputeCubemapSH(cubemap, decodeInstructions);
            s_CubemapCache[id] = new CubemapProbe { decodeInstructions = decodeInstructions, probe = sh };
            return true;
        }

        public static void ClearCache()
        {
            s_CubemapCache.Clear();
        }

        private struct CubemapProbe
        {
            public Vector4 decodeInstructions;
            public SphericalHarmonicsL2 probe;
        }

        /// <summary>Direction of sample <paramref name="index"/> of a Fibonacci sphere of <paramref name="count"/> points.</summary>
        public static Vector3 SphereSample(int index, int count)
        {
            float y = 1f - (index + 0.5f) * 2f / count;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = index * Mathf.PI * (3f - Mathf.Sqrt(5f));
            return new Vector3(Mathf.Cos(phi) * radius, y, Mathf.Sin(phi) * radius);
        }

        /// <summary>
        /// Adds one radiance sample to an ambient probe: <c>L * Ylm * basis * (A_l / pi) * solidAngle</c>.
        /// This is the only place the probe convention is implemented, so daytime dome projection and night HDRI
        /// projection cannot drift apart.
        /// </summary>
        /// <param name="sh">Probe being accumulated.</param>
        /// <param name="radiance">Linear radiance arriving from <paramref name="direction"/>.</param>
        /// <param name="direction">Normalised sample direction.</param>
        /// <param name="solidAngle">Solid angle represented by this sample.</param>
        public static void AccumulateAmbient(ref SphericalHarmonicsL2 sh, Vector3 radiance, Vector3 direction, float solidAngle)
        {
            float x = direction.x;
            float y = direction.y;
            float z = direction.z;

            float b0 = kBasis[0];
            float b1 = kBasis[1] * y;
            float b2 = kBasis[2] * z;
            float b3 = kBasis[3] * x;
            float b4 = kBasis[4] * x * y;
            float b5 = kBasis[5] * y * z;
            float b6 = kBasis[6] * (3f * z * z - 1f);
            float b7 = kBasis[7] * x * z;
            float b8 = kBasis[8] * (x * x - y * y);

            Accumulate(ref sh, radiance, 0, b0, solidAngle);
            Accumulate(ref sh, radiance, 1, b1, solidAngle);
            Accumulate(ref sh, radiance, 2, b2, solidAngle);
            Accumulate(ref sh, radiance, 3, b3, solidAngle);
            Accumulate(ref sh, radiance, 4, b4, solidAngle);
            Accumulate(ref sh, radiance, 5, b5, solidAngle);
            Accumulate(ref sh, radiance, 6, b6, solidAngle);
            Accumulate(ref sh, radiance, 7, b7, solidAngle);
            Accumulate(ref sh, radiance, 8, b8, solidAngle);
        }

        private static void Accumulate(ref SphericalHarmonicsL2 sh, Vector3 radiance, int coefficient, float basis, float solidAngle)
        {
            float factor = basis * kBasis[coefficient] * kCosineLobe[coefficient] * solidAngle;
            sh[0, coefficient] += radiance.x * factor;
            sh[1, coefficient] += radiance.y * factor;
            sh[2, coefficient] += radiance.z * factor;
        }

        private static SphericalHarmonicsL2 ComputeCubemapSH(Cubemap cubemap, Vector4 decodeInstructions)
        {
            SphericalHarmonicsL2 sh = new SphericalHarmonicsL2();

            if (!cubemap.isReadable)
            {
                Color defaultSky = Color.gray * 0.8f;
                Color defaultEquator = Color.gray * 0.5f;
                Color defaultGround = Color.gray * 0.2f;
                return FromTrilight(defaultSky, defaultEquator, defaultGround);
            }

            const int resolution = k_CubemapFaceResolution;
            float texel = 2f / resolution;
            float texelSolidAngle = texel * texel;

            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                CubemapFace face = (CubemapFace)faceIndex;

                for (int y = 0; y < resolution; y++)
                {
                    float v = texel * (y + 0.5f) - 1f;

                    for (int x = 0; x < resolution; x++)
                    {
                        float u = texel * (x + 0.5f) - 1f;

                        // Exact solid angle of the texel: the cube face is the unit plane z = 1, so a du x dv patch
                        // subtends du * dv / |(u,v,1)|^3.
                        float solidAngle = texelSolidAngle / Mathf.Pow(1f + u * u + v * v, 1.5f);

                        int tx = Mathf.Clamp((int)((u + 1f) * 0.5f * cubemap.width), 0, cubemap.width - 1);
                        int ty = Mathf.Clamp((int)((v + 1f) * 0.5f * cubemap.height), 0, cubemap.height - 1);

                        Vector3 radiance = DecodeCubemapSample(cubemap, cubemap.GetPixel(face, tx, ty), decodeInstructions);
                        AccumulateAmbient(ref sh, radiance, FaceDirection(face, u, v), solidAngle);
                    }
                }
            }

            return sh;
        }

        /// <summary>
        /// Direction a cubemap face point <paramref name="u"/>,<paramref name="v"/> in [-1, 1] samples, using the
        /// OpenGL/D3D cube map convention Unity's GPU sampler uses: <c>sc</c>/<c>tc</c> are <c>u</c>/<c>v</c>, and the
        /// major axis of each face is fixed. Note that <c>tc</c> runs against world Y on the four side faces and
        /// against world Z on the +Y/-Y faces, which is why this cannot be written as one sign flip of the whole cube.
        /// </summary>
        private static Vector3 FaceDirection(CubemapFace face, float u, float v)
        {
            Vector3 dir;

            switch (face)
            {
                case CubemapFace.PositiveX:
                    dir = new Vector3(1f, -v, -u);
                    break;
                case CubemapFace.NegativeX:
                    dir = new Vector3(-1f, -v, u);
                    break;
                case CubemapFace.PositiveY:
                    dir = new Vector3(u, 1f, v);
                    break;
                case CubemapFace.NegativeY:
                    dir = new Vector3(u, -1f, -v);
                    break;
                case CubemapFace.PositiveZ:
                    dir = new Vector3(u, -v, 1f);
                    break;
                default:
                    dir = new Vector3(-u, -v, -1f);
                    break;
            }

            return dir.normalized;
        }

        /// <summary>
        /// Turns a <see cref="Cubemap.GetPixel"/> value into the linear radiance the skybox shader samples, i.e.
        /// <c>DecodeHDREnvironment(sampler(_Tex), _Tex_HDR)</c> in EntityLighting.hlsl.
        /// <para>
        /// Two decodes stack here and the order matters. <c>GetPixel</c> returns the stored texel, which for an
        /// sRGB-flagged texture is still sRGB-encoded: the hardware sampler is what linearises it, so the CPU has to do
        /// the same first. Alpha is never gamma-encoded by the hardware. Unity's RGBM vector then scales the result
        /// (<c>maxRange * alpha^exponent</c>). Skipping the linearisation over-brightens a real HDR cubemap by ~25%.
        /// </para>
        /// </summary>
        private static Vector3 DecodeCubemapSample(Cubemap cubemap, Color raw, Vector4 decodeInstructions)
        {
            Vector3 rgb = cubemap.isDataSRGB ? DecodeGamma(raw) : new Vector3(raw.r, raw.g, raw.b);

            if (decodeInstructions != Vector4.zero)
            {
                float alpha = Mathf.Max(decodeInstructions.w * (raw.a - 1f) + 1f, 0f);
                rgb *= decodeInstructions.x * Mathf.Pow(alpha, decodeInstructions.y);
            }

            return rgb;
        }
    }
}
