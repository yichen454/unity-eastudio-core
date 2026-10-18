using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace EAStudio.Core.Editor.TextureAnalysis
{
    public static class TextureScanner
    {
        public static TextureReport Scan(TextureScanScope scope, Action<float, string> progressCallback = null)
        {
            var report = new TextureReport
            {
                ScanTime = DateTime.Now,
                Scope = scope
            };

            var scenePaths = GetScenePaths(scope);
            report.ScannedScenePaths = scenePaths;

            if (scenePaths.Count == 0)
                return report;

            var textureToScenesMap = new Dictionary<string, HashSet<string>>();

            for (int i = 0; i < scenePaths.Count; i++)
            {
                string scenePath = scenePaths[i];
                progressCallback?.Invoke((float)i / scenePaths.Count, $"Scanning dependencies: {Path.GetFileName(scenePath)}");

                string[] deps = AssetDatabase.GetDependencies(scenePath, true);
                foreach (var dep in deps)
                {
                    Type mainType = AssetDatabase.GetMainAssetTypeAtPath(dep);
                    if (mainType != null && typeof(Texture).IsAssignableFrom(mainType))
                    {
                        if (!textureToScenesMap.TryGetValue(dep, out var sceneSet))
                        {
                            sceneSet = new HashSet<string>();
                            textureToScenesMap[dep] = sceneSet;
                        }
                        sceneSet.Add(scenePath);
                    }
                }
            }

            // Also check textures from currently loaded scenes (like runtime/lightmap/scene skybox)
            if (scope == TextureScanScope.CurrentScene)
            {
                CollectActiveSceneSpecificTextures(textureToScenesMap);
            }

            int texIndex = 0;
            int totalTextures = textureToScenesMap.Count;

            foreach (var kvp in textureToScenesMap)
            {
                string texPath = kvp.Key;
                HashSet<string> scenes = kvp.Value;

                progressCallback?.Invoke((float)texIndex / Math.Max(1, totalTextures), $"Analyzing: {Path.GetFileName(texPath)}");
                texIndex++;

                var entry = AnalyzeTexture(texPath, scenes);
                if (entry != null)
                {
                    report.Entries.Add(entry);
                }
            }

            // Check duplicate texture names
            var nameGroups = report.Entries.GroupBy(e => e.TextureName, StringComparer.OrdinalIgnoreCase);
            foreach (var group in nameGroups)
            {
                if (group.Count() > 1)
                {
                    foreach (var entry in group)
                    {
                        entry.Issues |= TextureIssueFlags.DuplicateName;
                    }
                }
            }

            // Default sort by Memory Size Descending
            report.Entries.Sort((a, b) => b.MemorySizeBytes.CompareTo(a.MemorySizeBytes));

            return report;
        }

        private static List<string> GetScenePaths(TextureScanScope scope)
        {
            var list = new List<string>();

            if (scope == TextureScanScope.CurrentScene)
            {
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path))
                {
                    list.Add(active.path);
                }
            }
            else if (scope == TextureScanScope.BuildScenes)
            {
                foreach (var s in EditorBuildSettings.scenes)
                {
                    if (s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path))
                    {
                        list.Add(s.path);
                    }
                }

                // If no scenes enabled in build settings, fallback to all scenes in build settings or active scene
                if (list.Count == 0)
                {
                    foreach (var s in EditorBuildSettings.scenes)
                    {
                        if (!string.IsNullOrEmpty(s.path) && File.Exists(s.path))
                        {
                            list.Add(s.path);
                        }
                    }
                }

                if (list.Count == 0)
                {
                    Scene active = SceneManager.GetActiveScene();
                    if (!string.IsNullOrEmpty(active.path))
                    {
                        list.Add(active.path);
                    }
                }
            }

            return list;
        }

        private static void CollectActiveSceneSpecificTextures(Dictionary<string, HashSet<string>> textureToScenesMap)
        {
            Scene active = SceneManager.GetActiveScene();
            string activePath = string.IsNullOrEmpty(active.path) ? "Active Scene" : active.path;

            // Skybox
            if (RenderSettings.skybox != null)
            {
                int[] pids = RenderSettings.skybox.GetTexturePropertyNameIDs();
                foreach (int pid in pids)
                {
                    Texture tex = RenderSettings.skybox.GetTexture(pid);
                    if (tex != null)
                    {
                        string p = AssetDatabase.GetAssetPath(tex);
                        if (!string.IsNullOrEmpty(p))
                        {
                            if (!textureToScenesMap.TryGetValue(p, out var set))
                            {
                                set = new HashSet<string>();
                                textureToScenesMap[p] = set;
                            }
                            set.Add(activePath);
                        }
                    }
                }
            }

            // Lightmaps
            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            if (lightmaps != null)
            {
                foreach (var lm in lightmaps)
                {
                    if (lm == null) continue;
                    RegisterTexPath(lm.lightmapColor, activePath, textureToScenesMap);
                    RegisterTexPath(lm.lightmapDir, activePath, textureToScenesMap);
                    RegisterTexPath(lm.shadowMask, activePath, textureToScenesMap);
                }
            }
        }

        private static void RegisterTexPath(Texture tex, string scenePath, Dictionary<string, HashSet<string>> map)
        {
            if (tex == null) return;
            string p = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(p))
            {
                if (!map.TryGetValue(p, out var set))
                {
                    set = new HashSet<string>();
                    map[p] = set;
                }
                set.Add(scenePath);
            }
        }

        public static TextureUsageEntry AnalyzeTexture(string assetPath, IEnumerable<string> referencingScenes)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (tex == null) return null;

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            int width = tex.width;
            int height = tex.height;
            TextureFormat format = TextureFormat.RGBA32;
            int mipCount = 1;

            if (tex is Texture2D t2d)
            {
                width = t2d.width;
                height = t2d.height;
                format = t2d.format;
                mipCount = t2d.mipmapCount;
            }
            else if (tex is Cubemap cube)
            {
                width = cube.width;
                height = cube.height;
                format = cube.format;
                mipCount = cube.mipmapCount;
            }

            long runtimeMemory = Profiler.GetRuntimeMemorySizeLong(tex);
            long storageSize = 0;
            if (File.Exists(assetPath))
            {
                storageSize = new FileInfo(assetPath).Length;
            }

            var entry = new TextureUsageEntry
            {
                AssetPath = assetPath,
                TextureName = tex.name,
                Width = width,
                Height = height,
                Format = format,
                TextureType = importer != null ? importer.textureType : TextureImporterType.Default,
                HasMipmaps = mipCount > 1,
                MipmapCount = mipCount,
                IsReadable = importer != null && importer.isReadable,
                IsPowerOfTwo = Mathf.IsPowerOfTwo(width) && Mathf.IsPowerOfTwo(height),
                MemorySizeBytes = runtimeMemory,
                StorageSizeBytes = storageSize,
                ReferencingScenes = referencingScenes.ToList(),
                Issues = TextureIssueFlags.None
            };

            // Analyze Issues
            if (!entry.IsPowerOfTwo)
            {
                entry.Issues |= TextureIssueFlags.NonPowerOfTwo;
            }

            if (width >= 2048 || height >= 2048)
            {
                entry.Issues |= TextureIssueFlags.HighResolution;
            }

            if (entry.IsReadable)
            {
                entry.Issues |= TextureIssueFlags.ReadOnlyViolation;
            }

            if (IsUncompressedFormat(format))
            {
                entry.Issues |= TextureIssueFlags.Uncompressed;
            }

            // Mipmap checks:
            // 2D sprites/UI usually do not need mipmaps
            bool isUIOrSprite = entry.TextureType == TextureImporterType.Sprite || entry.TextureType == TextureImporterType.GUI;
            if (isUIOrSprite)
            {
                if (entry.HasMipmaps)
                {
                    entry.Issues |= TextureIssueFlags.UnnecessaryMipmaps;
                }
            }
            else
            {
                // 3D scene textures should usually have mipmaps
                if (!entry.HasMipmaps && entry.Width > 64 && entry.Height > 64)
                {
                    entry.Issues |= TextureIssueFlags.MissingMipmaps;
                }
            }

            return entry;
        }

        public static bool IsUncompressedFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.Alpha8:
                case TextureFormat.ARGB4444:
                case TextureFormat.RGB24:
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.RGB565:
                case TextureFormat.R16:
                case TextureFormat.RGBA4444:
                case TextureFormat.BGRA32:
                case TextureFormat.RHalf:
                case TextureFormat.RGHalf:
                case TextureFormat.RGBAHalf:
                case TextureFormat.RFloat:
                case TextureFormat.RGFloat:
                case TextureFormat.RGBAFloat:
                case TextureFormat.RGB9e5Float:
                case TextureFormat.RG16:
                case TextureFormat.R8:
                    return true;
                default:
                    return false;
            }
        }
    }
}
