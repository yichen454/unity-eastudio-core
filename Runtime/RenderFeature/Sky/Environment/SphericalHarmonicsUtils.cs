using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public static class SphericalHarmonicsUtils
    {
        private static readonly Dictionary<int, SphericalHarmonicsL2> s_CubemapCache = new Dictionary<int, SphericalHarmonicsL2>();

        /// <summary>
        /// Exact analytical rotation of Unity's SphericalHarmonicsL2 around the Y-axis.
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

                result[rgb, 7] = c2 * l7 + s2 * (l8 - 3f * l6);

                float delta = (1f - c2) * 0.25f * (3f * l6 - l8) - s2 * 0.25f * l7;
                result[rgb, 8] = l8 + delta;
                result[rgb, 6] = l6 - delta;
            }

            return result;
        }

        public static SphericalHarmonicsL2 Scale(SphericalHarmonicsL2 sh, Color tint, float intensity)
        {
            SphericalHarmonicsL2 result = new SphericalHarmonicsL2();
            float rMult = tint.r * intensity;
            float gMult = tint.g * intensity;
            float bMult = tint.b * intensity;

            for (int i = 0; i < 9; i++)
            {
                result[0, i] = sh[0, i] * rMult;
                result[1, i] = sh[1, i] * gMult;
                result[2, i] = sh[2, i] * bMult;
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

        public static bool ExtractFromCubemap(Cubemap cubemap, out SphericalHarmonicsL2 sh)
        {
            if (cubemap == null)
            {
                sh = default;
                return false;
            }

            int id = cubemap.GetInstanceID();
            if (s_CubemapCache.TryGetValue(id, out sh))
                return true;

            sh = ComputeCubemapSH(cubemap);
            s_CubemapCache[id] = sh;
            return true;
        }

        public static void ClearCache()
        {
            s_CubemapCache.Clear();
        }

        private static SphericalHarmonicsL2 ComputeCubemapSH(Cubemap cubemap)
        {
            SphericalHarmonicsL2 sh = new SphericalHarmonicsL2();

            if (cubemap.isReadable)
            {
                Vector3[] sampleDirections = GetSampleDirections();
                int count = sampleDirections.Length;
                float weight = (4f * Mathf.PI) / count;

                for (int i = 0; i < count; i++)
                {
                    Vector3 dir = sampleDirections[i];
                    Color col = SampleCubemap(cubemap, dir);

                    float y00 = 0.282095f;
                    float y1_1 = -0.488603f * dir.y;
                    float y10 = 0.488603f * dir.z;
                    float y11 = -0.488603f * dir.x;

                    float y2_2 = 1.092548f * dir.x * dir.y;
                    float y2_1 = -1.092548f * dir.y * dir.z;
                    float y20 = 0.315392f * (3f * dir.z * dir.z - 1f);
                    float y21 = -1.092548f * dir.x * dir.z;
                    float y22 = 0.546274f * (dir.x * dir.x - dir.y * dir.y);

                    float[] basis = new float[] { y00, y1_1, y10, y11, y2_2, y2_1, y20, y21, y22 };

                    for (int rgb = 0; rgb < 3; rgb++)
                    {
                        float val = col[rgb] * weight;
                        for (int b = 0; b < 9; b++)
                        {
                            sh[rgb, b] += val * basis[b];
                        }
                    }
                }

                float a0 = Mathf.PI;
                float a1 = (2f * Mathf.PI) / 3f;
                float a2 = Mathf.PI / 4f;

                for (int rgb = 0; rgb < 3; rgb++)
                {
                    sh[rgb, 0] *= a0 / (4f * Mathf.PI);
                    for (int b = 1; b <= 3; b++) sh[rgb, b] *= a1 / (4f * Mathf.PI);
                    for (int b = 4; b <= 8; b++) sh[rgb, b] *= a2 / (4f * Mathf.PI);
                }
            }
            else
            {
                Color defaultSky = Color.gray * 0.8f;
                Color defaultEquator = Color.gray * 0.5f;
                Color defaultGround = Color.gray * 0.2f;
                sh = FromTrilight(defaultSky, defaultEquator, defaultGround);
            }

            return sh;
        }

        private static Color SampleCubemap(Cubemap cubemap, Vector3 dir)
        {
            CubemapFace face;
            Vector2 uv;
            Vector3 absDir = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));

            if (absDir.x >= absDir.y && absDir.x >= absDir.z)
            {
                face = dir.x > 0 ? CubemapFace.PositiveX : CubemapFace.NegativeX;
                uv = dir.x > 0 ? new Vector2(-dir.z, dir.y) / absDir.x : new Vector2(dir.z, dir.y) / absDir.x;
            }
            else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
            {
                face = dir.y > 0 ? CubemapFace.PositiveY : CubemapFace.NegativeY;
                uv = dir.y > 0 ? new Vector2(dir.x, -dir.z) / absDir.y : new Vector2(dir.x, dir.z) / absDir.y;
            }
            else
            {
                face = dir.z > 0 ? CubemapFace.PositiveZ : CubemapFace.NegativeZ;
                uv = dir.z > 0 ? new Vector2(dir.x, dir.y) / absDir.z : new Vector2(-dir.x, dir.y) / absDir.z;
            }

            uv = (uv + Vector2.one) * 0.5f;
            int x = Mathf.Clamp((int)(uv.x * cubemap.width), 0, cubemap.width - 1);
            int y = Mathf.Clamp((int)(uv.y * cubemap.height), 0, cubemap.height - 1);
            return cubemap.GetPixel(face, x, y);
        }

        private static Vector3[] GetSampleDirections()
        {
            List<Vector3> list = new List<Vector3>(26);

            list.Add(new Vector3(1, 0, 0));
            list.Add(new Vector3(-1, 0, 0));
            list.Add(new Vector3(0, 1, 0));
            list.Add(new Vector3(0, -1, 0));
            list.Add(new Vector3(0, 0, 1));
            list.Add(new Vector3(0, 0, -1));

            float invSqrt2 = 0.70710678f;
            list.Add(new Vector3(invSqrt2, invSqrt2, 0));
            list.Add(new Vector3(-invSqrt2, invSqrt2, 0));
            list.Add(new Vector3(invSqrt2, -invSqrt2, 0));
            list.Add(new Vector3(-invSqrt2, -invSqrt2, 0));
            list.Add(new Vector3(invSqrt2, 0, invSqrt2));
            list.Add(new Vector3(-invSqrt2, 0, invSqrt2));
            list.Add(new Vector3(invSqrt2, 0, -invSqrt2));
            list.Add(new Vector3(-invSqrt2, 0, -invSqrt2));
            list.Add(new Vector3(0, invSqrt2, invSqrt2));
            list.Add(new Vector3(0, -invSqrt2, invSqrt2));
            list.Add(new Vector3(0, invSqrt2, -invSqrt2));
            list.Add(new Vector3(0, -invSqrt2, -invSqrt2));

            float invSqrt3 = 0.57735027f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        list.Add(new Vector3(x * invSqrt3, y * invSqrt3, z * invSqrt3));
                    }
                }
            }

            return list.ToArray();
        }
    }
}
