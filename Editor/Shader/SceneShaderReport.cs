using System;
using System.Collections.Generic;
using UnityEngine;

namespace EAStudio.Core.Editor.ShaderAnalysis
{
    public enum SceneTargetType
    {
        Renderer,
        Terrain,
        Graphic,
        Skybox,
        Volume,
        Other
    }

    public class SceneReferencingTarget
    {
        public UnityEngine.Object TargetObject;
        public GameObject HostGameObject;
        public string HierarchyPath;
        public SceneTargetType TargetType;
        public bool IsActiveInHierarchy;

        public SceneReferencingTarget(UnityEngine.Object targetObj, GameObject hostGo, string hierarchyPath, SceneTargetType targetType)
        {
            TargetObject = targetObj;
            HostGameObject = hostGo;
            HierarchyPath = hierarchyPath;
            TargetType = targetType;
            IsActiveInHierarchy = hostGo != null && hostGo.activeInHierarchy;
        }
    }

    public class KeywordImpactSummary
    {
        public string Keyword;
        public int AffectedMaterialCount;
        public int AffectedRendererCount;
        public int AffectedVariantCount;
        public bool IsMultiVariantCause; // True if disabling or enabling it splits materials into different variants
    }

    public class MaterialVariantUsage
    {
        public string KeywordSignature;
        public string[] Keywords;
        public List<Material> Materials = new List<Material>();
        public List<SceneReferencingTarget> ReferencingTargets = new List<SceneReferencingTarget>();
    }

    public class ShaderUsageEntry
    {
        public UnityEngine.Shader Shader;
        public string ShaderName;
        public string AssetPath;
        public int PassCount;
        public int SceneEstimatedPassVariants;
        public List<string> AllActiveKeywords = new List<string>();
        public List<KeywordImpactSummary> KeywordImpacts = new List<KeywordImpactSummary>();
        public List<MaterialVariantUsage> VariantUsages = new List<MaterialVariantUsage>();
        public List<Material> UniqueMaterials = new List<Material>();
        public List<SceneReferencingTarget> AllReferencingTargets = new List<SceneReferencingTarget>();

        public int SceneVariantCount => VariantUsages.Count;
        public int MaterialCount => UniqueMaterials.Count;
        public int ReferencerCount => AllReferencingTargets.Count;
    }

    public class SceneShaderReport
    {
        public DateTime ScanTime;
        public int TotalScenesScanned;
        public int TotalRenderersScanned;
        public int TotalMaterialsScanned;
        public int TotalUniqueShaders;
        public int TotalSceneVariants;
        public int TotalEstimatedPassVariants;
        public List<ShaderUsageEntry> Entries = new List<ShaderUsageEntry>();
    }
}
