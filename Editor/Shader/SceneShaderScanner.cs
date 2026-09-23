using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EAStudio.Core.Editor.ShaderAnalysis
{
    public static class SceneShaderScanner
    {
        private static MethodInfo s_GetShaderTotalPassCountMethod;
        private static bool s_MethodLookupAttempted;

        public static SceneShaderReport ScanActiveScenes()
        {
            var report = new SceneShaderReport
            {
                ScanTime = DateTime.Now
            };

            var shaderMap = new Dictionary<UnityEngine.Shader, ShaderDataAccumulator>();
            var scannedRenderers = 0;
            var scannedMaterials = new HashSet<Material>();

            int sceneCount = SceneManager.sceneCount;
            report.TotalScenesScanned = sceneCount;

            for (int s = 0; s < sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;

                GameObject[] rootObjects = scene.GetRootGameObjects();

                // 1. Scan all Renderers
                foreach (var root in rootObjects)
                {
                    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                    foreach (var rend in renderers)
                    {
                        scannedRenderers++;
                        if (rend == null)
                            continue;

                        Material[] sharedMaterials = rend.sharedMaterials;
                        if (sharedMaterials == null || sharedMaterials.Length == 0)
                            continue;

                        var target = new SceneReferencingTarget(
                            rend,
                            rend.gameObject,
                            GetHierarchyPath(rend.transform),
                            SceneTargetType.Renderer
                        );

                        foreach (var mat in sharedMaterials)
                        {
                            if (mat == null)
                                continue;
                            scannedMaterials.Add(mat);
                            RegisterMaterial(shaderMap, mat, target);
                        }
                    }
                }

                // 2. Scan Terrains
                foreach (var root in rootObjects)
                {
                    Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
                    foreach (var terrain in terrains)
                    {
                        if (terrain == null || terrain.materialTemplate == null)
                            continue;

                        var target = new SceneReferencingTarget(
                            terrain,
                            terrain.gameObject,
                            GetHierarchyPath(terrain.transform),
                            SceneTargetType.Terrain
                        );

                        scannedMaterials.Add(terrain.materialTemplate);
                        RegisterMaterial(shaderMap, terrain.materialTemplate, target);
                    }
                }

                // 3. Scan UI Graphics
                foreach (var root in rootObjects)
                {
                    Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
                    foreach (var graphic in graphics)
                    {
                        if (graphic == null)
                            continue;

                        Material mat = graphic.material;
                        if (mat == null || mat == graphic.defaultMaterial)
                            continue;

                        var target = new SceneReferencingTarget(
                            graphic,
                            graphic.gameObject,
                            GetHierarchyPath(graphic.transform),
                            SceneTargetType.Graphic
                        );

                        scannedMaterials.Add(mat);
                        RegisterMaterial(shaderMap, mat, target);
                    }
                }

                // 4. Scan Volumes
                foreach (var root in rootObjects)
                {
                    Volume[] volumes = root.GetComponentsInChildren<Volume>(true);
                    foreach (var vol in volumes)
                    {
                        if (vol == null || vol.sharedProfile == null)
                            continue;

                        var components = vol.sharedProfile.components;
                        if (components == null)
                            continue;

                        foreach (var comp in components)
                        {
                            if (comp == null)
                                continue;

                            var matProps = comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                                .Where(f => typeof(Material).IsAssignableFrom(f.FieldType));

                            foreach (var field in matProps)
                            {
                                var mat = field.GetValue(comp) as Material;
                                if (mat != null)
                                {
                                    var target = new SceneReferencingTarget(
                                        vol,
                                        vol.gameObject,
                                        GetHierarchyPath(vol.transform),
                                        SceneTargetType.Volume
                                    );
                                    scannedMaterials.Add(mat);
                                    RegisterMaterial(shaderMap, mat, target);
                                }
                            }
                        }
                    }
                }
            }

            // 5. Scan Scene Skybox
            if (RenderSettings.skybox != null)
            {
                Material skyboxMat = RenderSettings.skybox;
                scannedMaterials.Add(skyboxMat);
                var target = new SceneReferencingTarget(
                    skyboxMat,
                    null,
                    "Scene Settings / RenderSettings.skybox",
                    SceneTargetType.Skybox
                );
                RegisterMaterial(shaderMap, skyboxMat, target);
            }

            // Assemble report entries
            int totalVariants = 0;
            int totalEstimatedPassVariants = 0;

            foreach (var kvp in shaderMap)
            {
                UnityEngine.Shader shader = kvp.Key;
                ShaderDataAccumulator acc = kvp.Value;

                int passCount = GetShaderPassCount(shader);

                var entry = new ShaderUsageEntry
                {
                    Shader = shader,
                    ShaderName = shader != null ? shader.name : "Missing/Null Shader",
                    AssetPath = shader != null ? AssetDatabase.GetAssetPath(shader) : string.Empty,
                    PassCount = passCount,
                    UniqueMaterials = acc.UniqueMaterials.ToList(),
                    AllReferencingTargets = acc.AllTargets.ToList()
                };

                foreach (var variantKvp in acc.VariantMap)
                {
                    entry.VariantUsages.Add(new MaterialVariantUsage
                    {
                        KeywordSignature = variantKvp.Key,
                        Keywords = variantKvp.Value.Keywords,
                        Materials = variantKvp.Value.Materials.ToList(),
                        ReferencingTargets = variantKvp.Value.Targets.ToList()
                    });
                }

                // Sort variants by referencing target count desc
                entry.VariantUsages.Sort((a, b) => b.ReferencingTargets.Count.CompareTo(a.ReferencingTargets.Count));

                // Compute keyword impacts across all variants for this shader
                var keywordSet = new HashSet<string>();
                var keywordToMatCount = new Dictionary<string, HashSet<Material>>();
                var keywordToTargetCount = new Dictionary<string, HashSet<SceneReferencingTarget>>();
                var keywordToVariantCount = new Dictionary<string, int>();

                foreach (var vu in entry.VariantUsages)
                {
                    if (vu.Keywords == null)
                        continue;

                    foreach (var kw in vu.Keywords)
                    {
                        keywordSet.Add(kw);

                        if (!keywordToMatCount.TryGetValue(kw, out var mats))
                        {
                            mats = new HashSet<Material>();
                            keywordToMatCount[kw] = mats;
                        }
                        foreach (var m in vu.Materials) mats.Add(m);

                        if (!keywordToTargetCount.TryGetValue(kw, out var tgts))
                        {
                            tgts = new HashSet<SceneReferencingTarget>();
                            keywordToTargetCount[kw] = tgts;
                        }
                        foreach (var t in vu.ReferencingTargets) tgts.Add(t);

                        keywordToVariantCount[kw] = keywordToVariantCount.TryGetValue(kw, out int count) ? count + 1 : 1;
                    }
                }

                entry.AllActiveKeywords = keywordSet.OrderBy(k => k).ToList();

                foreach (var kw in entry.AllActiveKeywords)
                {
                    int matCount = keywordToMatCount.TryGetValue(kw, out var mats) ? mats.Count : 0;
                    int tgtCount = keywordToTargetCount.TryGetValue(kw, out var tgts) ? tgts.Count : 0;
                    int vCount = keywordToVariantCount.TryGetValue(kw, out int c) ? c : 0;

                    entry.KeywordImpacts.Add(new KeywordImpactSummary
                    {
                        Keyword = kw,
                        AffectedMaterialCount = matCount,
                        AffectedRendererCount = tgtCount,
                        AffectedVariantCount = vCount,
                        // If some materials have this keyword and some materials of the same shader don't, it directly splits variants!
                        IsMultiVariantCause = matCount > 0 && matCount < entry.MaterialCount
                    });
                }

                // Sort keyword impacts: keywords causing variant split first, then by affected renderer count desc
                entry.KeywordImpacts.Sort((a, b) =>
                {
                    if (a.IsMultiVariantCause != b.IsMultiVariantCause)
                        return b.IsMultiVariantCause.CompareTo(a.IsMultiVariantCause);
                    return b.AffectedRendererCount.CompareTo(a.AffectedRendererCount);
                });

                entry.SceneEstimatedPassVariants = entry.VariantUsages.Count * Math.Max(1, passCount);

                totalVariants += entry.VariantUsages.Count;
                totalEstimatedPassVariants += entry.SceneEstimatedPassVariants;
                report.Entries.Add(entry);
            }

            // Sort entries by reference count desc
            report.Entries.Sort((a, b) => b.ReferencerCount.CompareTo(a.ReferencerCount));

            report.TotalRenderersScanned = scannedRenderers;
            report.TotalMaterialsScanned = scannedMaterials.Count;
            report.TotalUniqueShaders = report.Entries.Count;
            report.TotalSceneVariants = totalVariants;
            report.TotalEstimatedPassVariants = totalEstimatedPassVariants;

            return report;
        }

        private static void RegisterMaterial(
            Dictionary<UnityEngine.Shader, ShaderDataAccumulator> shaderMap,
            Material mat,
            SceneReferencingTarget target)
        {
            if (mat == null)
                return;

            UnityEngine.Shader shader = mat.shader;
            if (shader == null)
                return;

            if (!shaderMap.TryGetValue(shader, out var acc))
            {
                acc = new ShaderDataAccumulator();
                shaderMap[shader] = acc;
            }

            acc.UniqueMaterials.Add(mat);
            acc.AllTargets.Add(target);

            string[] keywords = GetSortedKeywords(mat);
            string signature = keywords.Length > 0 ? string.Join(" ", keywords) : "[No Keywords / Default]";

            if (!acc.VariantMap.TryGetValue(signature, out var varAcc))
            {
                varAcc = new VariantAccumulator { Keywords = keywords };
                acc.VariantMap[signature] = varAcc;
            }

            varAcc.Materials.Add(mat);
            varAcc.Targets.Add(target);
        }

        private static string[] GetSortedKeywords(Material mat)
        {
            if (mat == null)
                return Array.Empty<string>();

            var list = new List<string>();

            // 1. material.shaderKeywords (classic / local)
            string[] directKeywords = mat.shaderKeywords;
            if (directKeywords != null)
            {
                for (int i = 0; i < directKeywords.Length; i++)
                {
                    string kw = directKeywords[i].Trim();
                    if (!string.IsNullOrEmpty(kw) && !list.Contains(kw))
                        list.Add(kw);
                }
            }

            // 2. enabledLocalKeywords (Unity 2021.2+ / Unity 6 LocalKeyword API)
            try
            {
                var enabledLocalKeywords = mat.enabledKeywords;
                if (enabledLocalKeywords != null)
                {
                    foreach (var kw in enabledLocalKeywords)
                    {
                        string kwName = kw.name.Trim();
                        if (!string.IsNullOrEmpty(kwName) && !list.Contains(kwName))
                            list.Add(kwName);
                    }
                }
            }
            catch
            {
                // Fallback gracefully
            }

            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        public static int GetShaderPassCount(UnityEngine.Shader shader)
        {
            if (shader == null)
                return 1;

            EnsureMethodLookup();

            if (s_GetShaderTotalPassCountMethod != null)
            {
                try
                {
                    object result = s_GetShaderTotalPassCountMethod.Invoke(null, new object[] { shader, 0 });
                    if (result is int count && count > 0)
                        return count;
                }
                catch
                {
                    // Fallback
                }
            }

            return shader.passCount > 0 ? shader.passCount : 1;
        }

        private static void EnsureMethodLookup()
        {
            if (s_MethodLookupAttempted)
                return;

            s_MethodLookupAttempted = true;

            try
            {
                var shaderUtilType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ShaderUtil");
                if (shaderUtilType != null)
                {
                    s_GetShaderTotalPassCountMethod = shaderUtilType.GetMethod(
                        "GetShaderTotalPassCount",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(UnityEngine.Shader), typeof(int) },
                        null
                    );
                }
            }
            catch
            {
                // Fallback gracefully
            }
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null)
                return string.Empty;

            var stack = new Stack<string>();
            Transform current = t;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", stack);
        }

        private class ShaderDataAccumulator
        {
            public HashSet<Material> UniqueMaterials = new HashSet<Material>();
            public HashSet<SceneReferencingTarget> AllTargets = new HashSet<SceneReferencingTarget>();
            public Dictionary<string, VariantAccumulator> VariantMap = new Dictionary<string, VariantAccumulator>();
        }

        private class VariantAccumulator
        {
            public string[] Keywords;
            public HashSet<Material> Materials = new HashSet<Material>();
            public HashSet<SceneReferencingTarget> Targets = new HashSet<SceneReferencingTarget>();
        }
    }
}
