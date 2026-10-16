using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    public enum BloomTextureOutput { Composite, HaloOnly }

    [Serializable]
    public sealed class BloomTextureSettings
    {
        public float Threshold;
        public float SoftKnee = 0.1f;
        public float Intensity = 1;
        public int Radius = 24;
        public Color Tint = Color.white;
        public int ExtraPadding;
        public float Exposure;
        public BloomTextureOutput Output;
    }

    public sealed class BloomTextureResult : IDisposable
    {
        public Texture2D Texture { get; internal set; }
        public bool HasTransparency { get; internal set; }
        public int Padding { get; internal set; }
        public float ClippedFraction { get; internal set; }
        public bool HaloTouchesEdge { get; internal set; }
        public Rect SourceRect { get; internal set; }

        public void Dispose()
        {
            if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }
    }

    /// <summary>Editor-only, linear-energy bloom with straight-alpha PNG output.</summary>
    public static class BloomTextureGenerator
    {
        public static BloomTextureResult Generate(Texture2D source, BloomTextureSettings settings, bool showProgress = true)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Validate(settings);
            if (!SystemInfo.supportsComputeShaders ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                throw new NotSupportedException("Bloom requires compute shaders and RGBAFloat random-write textures.");
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Default &&
                importer.textureType != TextureImporterType.Sprite &&
                importer.textureType != TextureImporterType.GUI)
                throw new ArgumentException($"The selected texture uses the {importer.textureType} import type. Select a Default, Sprite or GUI color texture.");
            if (source.dimension != UnityEngine.Rendering.TextureDimension.Tex2D)
                throw new ArgumentException("Select a 2D color texture.");

            var shader = LoadShader();
            RenderTexture snapshot = null, halo = null, scratch = null;
            ComputeBuffer stats = null;
            Texture2D output = null;
            try
            {
                CheckSize(source.width, source.height, source.width, source.height);
                snapshot = CreateTarget(source.width, source.height);
                stats = new ComputeBuffer(6, sizeof(uint));
                stats.SetData(new uint[6]);
                shader.SetInts("_SourceSize", source.width, source.height);
                bool srgbData = importer != null ? importer.sRGBTexture : source.isDataSRGB;
                shader.SetInt("_DecodeSourceSRGB", srgbData &&
                    !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat) ? 1 : 0);
                int kernel = shader.FindKernel("Snapshot");
                shader.SetBuffer(kernel, "_Stats", stats);
                shader.SetTexture(kernel, "_Source", source);
                shader.SetTexture(kernel, "_Result", snapshot);
                Dispatch(shader, kernel, source.width, source.height, 0.05f, showProgress);
                var counters = new uint[6];
                stats.GetData(counters);
                if (counters[1] != 0) throw new ArgumentException("Source must contain finite RGB values within +/-65504.");
                if (counters[2] != source.width || counters[3] != source.height)
                    throw new InvalidOperationException("Source mip limits changed its resident dimensions. Load its full-resolution mip before generating.");
                bool transparent = counters[0] != 0;
                bool bloomEnabled = settings.Intensity > 0 && settings.Tint.maxColorComponent > 0;
                int boxRadius = (settings.Radius + 2) / 3;
                int padding = settings.ExtraPadding;
                if (bloomEnabled && boxRadius > 0 && (transparent || settings.Output == BloomTextureOutput.HaloOnly))
                    padding += settings.Radius + 1;
                int width = checked(source.width + 2 * padding);
                int height = checked(source.height + 2 * padding);
                CheckSize(width, height, source.width, source.height);
                halo = CreateTarget(width, height);
                scratch = CreateTarget(width, height);
                shader.SetInts("_Size", width, height);
                shader.SetInt("_Padding", padding);
                shader.SetFloat("_Threshold", settings.Threshold);
                shader.SetFloat("_Knee", settings.SoftKnee);
                kernel = shader.FindKernel("Extract");
                shader.SetTexture(kernel, "_Snapshot", snapshot);
                shader.SetTexture(kernel, "_Result", halo);
                Dispatch(shader, kernel, width, height, 0.15f, showProgress);
                kernel = shader.FindKernel("Blur");
                for (int pass = 0; pass < 6 && boxRadius > 0 && bloomEnabled; pass++)
                {
                    int passRadius = (settings.Radius + 2 - pass / 2) / 3;
                    if (passRadius == 0) continue;
                    shader.SetInt("_BoxRadius", passRadius);
                    shader.SetInts("_Direction", pass % 2 == 0 ? 1 : 0, pass % 2 == 0 ? 0 : 1);
                    shader.SetTexture(kernel, "_Input", halo);
                    shader.SetTexture(kernel, "_Result", scratch);
                    Dispatch(shader, kernel, width, height, 0.2f + pass * 0.1f, showProgress);
                    var swap = halo; halo = scratch; scratch = swap;
                }
                kernel = shader.FindKernel("Compose");
                shader.SetInt("_HaloOnly", settings.Output == BloomTextureOutput.HaloOnly ? 1 : 0);
                shader.SetFloat("_Intensity", settings.Intensity);
                Color tint = settings.Tint.linear;
                shader.SetVector("_Tint", new Vector4(tint.r, tint.g, tint.b, 0));
                shader.SetFloat("_Exposure", Mathf.Pow(2, settings.Exposure));
                shader.SetBuffer(kernel, "_Stats", stats);
                shader.SetTexture(kernel, "_Snapshot", snapshot);
                shader.SetTexture(kernel, "_Input", halo);
                shader.SetTexture(kernel, "_Result", scratch);
                Dispatch(shader, kernel, width, height, 0.85f, showProgress);
                stats.GetData(counters);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = scratch;
                    // Compute already encoded RGB. Readback must not perform another conversion.
                    output = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                    { name = source.name + "_Bloom", hideFlags = HideFlags.HideAndDontSave };
                    output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    output.Apply(false, false);
                }
                finally { RenderTexture.active = previous; }
                var result = new BloomTextureResult
                {
                    Texture = output, HasTransparency = transparent, Padding = padding,
                    ClippedFraction = counters[4] / (float)((long)width * height),
                    HaloTouchesEdge = counters[5] != 0,
                    SourceRect = new Rect(padding / (float)width, padding / (float)height,
                        source.width / (float)width, source.height / (float)height)
                };
                output = null;
                return result;
            }
            finally
            {
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
                Release(snapshot); Release(halo); Release(scratch);
                stats?.Dispose();
                if (showProgress) EditorUtility.ClearProgressBar();
            }
        }

        public static void Export(BloomTextureResult result, Texture2D source, string assetPath, bool overwrite = false)
        {
            if (result?.Texture == null) throw new ArgumentException("Generate a result before exporting.");
            string root = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(assetPath);
            if (!fullPath.StartsWith(root, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a PNG destination inside Assets.");
            assetPath = "Assets/" + fullPath.Substring(root.Length).Replace('\\', '/');
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (!string.IsNullOrEmpty(sourcePath) && string.Equals(Path.GetFullPath(sourcePath), fullPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The source texture cannot be overwritten.");
            bool existed = File.Exists(fullPath);
            if (!overwrite && (existed || File.Exists(fullPath + ".meta")))
                throw new IOException("Destination already exists.");
            if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
                throw new DirectoryNotFoundException("Choose an existing Assets folder.");
            byte[] oldBytes = existed ? File.ReadAllBytes(fullPath) : null;
            byte[] oldMeta = File.Exists(fullPath + ".meta") ? File.ReadAllBytes(fullPath + ".meta") : null;
            string stage = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Texture2D decoded = null;
            try
            {
                var bytes = result.Texture.EncodeToPNG();
                File.WriteAllBytes(stage, bytes);
                decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(stage)) ||
                    decoded.width != result.Texture.width || decoded.height != result.Texture.height)
                    throw new IOException("PNG staging verification failed.");
                var originalPixels = result.Texture.GetPixels32();
                var decodedPixels = decoded.GetPixels32();
                for (int i = 0; i < originalPixels.Length; i++)
                    if (!originalPixels[i].Equals(decodedPixels[i]))
                        throw new IOException("PNG staging changed RGBA pixel data.");
                if (existed) File.Replace(stage, fullPath, null);
                else File.Move(stage, fullPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) throw new IOException("PNG importer was not created.");
                // Platform overrides may resize or discard alpha even when default settings are valid.
                foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL", "Windows Store Apps", "tvOS", "PS4", "PS5", "XboxOne", "Nintendo Switch" })
                    importer.ClearPlatformTextureSettings(platform);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.mipmapEnabled = false;
                importer.streamingMipmaps = false;
                importer.ignoreMipmapLimit = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(decoded.width, decoded.height));
                var defaults = importer.GetDefaultPlatformTextureSettings();
                defaults.format = TextureImporterFormat.RGBA32;
                defaults.maxTextureSize = importer.maxTextureSize;
                defaults.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(defaults);
                importer.isReadable = false;
                importer.SaveAndReimport();
                var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (saved == null || saved.width != decoded.width || saved.height != decoded.height ||
                    !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(saved.graphicsFormat))
                    throw new IOException("Imported PNG dimensions or RGBA format did not match the result.");
            }
            catch
            {
                if (oldBytes != null) File.WriteAllBytes(fullPath, oldBytes);
                else if (File.Exists(fullPath)) File.Delete(fullPath);
                if (oldMeta != null) File.WriteAllBytes(fullPath + ".meta", oldMeta);
                else if (File.Exists(fullPath + ".meta")) File.Delete(fullPath + ".meta");
                if (existed) AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                else AssetDatabase.Refresh();
                throw;
            }
            finally
            {
                if (File.Exists(stage)) File.Delete(stage);
                if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
            }
        }

        private static void Validate(BloomTextureSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            foreach (float value in new[] { s.Threshold, s.SoftKnee, s.Intensity, s.Exposure, s.Tint.r, s.Tint.g, s.Tint.b })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Settings must be finite.");
            if (s.Threshold < 0 || s.Threshold > 65504 || s.SoftKnee < 0 || s.SoftKnee > 65504 || s.Intensity < 0 || s.Intensity > 100 ||
                s.Radius < 0 || s.Radius > 256 || s.ExtraPadding < 0 || s.ExtraPadding > 4096 ||
                s.Exposure < -16 || s.Exposure > 16 || s.Tint.r < 0 || s.Tint.g < 0 || s.Tint.b < 0 ||
                s.Tint.r > 1 || s.Tint.g > 1 || s.Tint.b > 1 || !Enum.IsDefined(typeof(BloomTextureOutput), s.Output))
                throw new ArgumentOutOfRangeException(nameof(s), "Settings are outside the supported range.");
        }

        private static void CheckSize(int width, int height, int sourceWidth, int sourceHeight)
        {
            // Include readback, PNG and staging allocations as well as GPU targets.
            long estimatedBytes = 16L * sourceWidth * sourceHeight + 64L * width * height;
            if (width <= 0 || height <= 0 || width > Mathf.Min(16384, SystemInfo.maxTextureSize) ||
                height > Mathf.Min(16384, SystemInfo.maxTextureSize) || estimatedBytes > 512L * 1024 * 1024)
                throw new ArgumentException("Output exceeds the GPU size limit or the 512 MiB working-memory budget. Reduce source size, radius or extra padding.");
        }

        private static ComputeShader LoadShader()
        {
            string scriptPath = null;
            foreach (string guid in AssetDatabase.FindAssets("BloomTextureGenerator t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/BloomTextureGenerator.cs", StringComparison.Ordinal)) { scriptPath = path; break; }
            }
            var shader = scriptPath == null ? null : AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Path.GetDirectoryName(scriptPath).Replace('\\', '/') + "/Shaders/BloomTexture.compute");
            if (shader == null) throw new InvalidOperationException("BloomTexture.compute could not be located beside the generator.");
            return shader;
        }

        private static RenderTexture CreateTarget(int width, int height)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave };
            if (target.Create()) return target;
            UnityEngine.Object.DestroyImmediate(target);
            throw new InvalidOperationException("Could not allocate bloom render target.");
        }

        private static void Dispatch(ComputeShader shader, int kernel, int width, int height, float progress, bool showProgress)
        {
            if (showProgress && EditorUtility.DisplayCancelableProgressBar("Bloom Texture Generator", "Processing full-resolution bloom", progress))
                throw new OperationCanceledException("Bloom generation cancelled.");
            shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
        }

        private static void Release(RenderTexture target)
        {
            if (target == null) return;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
