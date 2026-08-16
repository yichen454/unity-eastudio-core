using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
public class AssetFixerWindow : EditorWindow
{
    private const float MinListHeight = 220f;
    private const float ListBottomReserve = 320f;
    private const float MaxListHeightRatio = 0.62f;
    private static readonly string[] AssetTabs =
    {
        "Prefab",
        "Layer"
    };


    private struct TextureExportRule
    {
        public readonly string Property;
        public readonly string Suffix;

        public TextureExportRule(
            string property,
            string suffix)
        {
            Property = property;
            Suffix = suffix;
        }
    }

    private static readonly TextureExportRule[] TextureExportRules =
    {
        new TextureExportRule("_MainTex", "D"),
        new TextureExportRule("_BaseMap", "D"),
        new TextureExportRule("_BaseColorMap", "D"),
        new TextureExportRule("_BumpMap", "N"),
        new TextureExportRule("_NormalMap", "N"),
        new TextureExportRule("_MaskMap", "M"),
        new TextureExportRule("_OcclusionMap", "AO"),
        new TextureExportRule("_MetallicGlossMap", "MT"),
        new TextureExportRule("_EmissionMap", "E"),
        new TextureExportRule("_DetailMask", "A")
    };

    private Terrain sourceTerrain;
    private GameObject sourceSceneObject;
    private DefaultAsset outputRootFolder;
    private bool replaceSceneInstances;
    private Vector2 prefabListScroll;
    private Vector2 layerListScroll;
    private int assetTabIndex;
    private Dictionary<string, bool> prefabSelections =
        new Dictionary<string, bool>();
    private Dictionary<string, string> prefabNameOverrides =
        new Dictionary<string, string>();
    private Dictionary<string, bool> layerSelections =
        new Dictionary<string, bool>();
    private Dictionary<string, string> layerNameOverrides =
        new Dictionary<string, string>();

    [MenuItem("Tools/EAStudio/资产/场景资产整理与修复")]
    static void Open()
    {
        GetWindow<AssetFixerWindow>("场景资产整理与修复");
    }

    private void OnGUI()
    {
        SyncSelectionStates();

        GUILayout.Label("Source Terrain", EditorStyles.boldLabel);

        sourceTerrain = (Terrain)EditorGUILayout.ObjectField(
            sourceTerrain,
            typeof(Terrain),
            true);

        GUILayout.Space(8);
        GUILayout.Label("Optional Scene Object", EditorStyles.boldLabel);

        sourceSceneObject = (GameObject)EditorGUILayout.ObjectField(
            sourceSceneObject,
            typeof(GameObject),
            true);

        GUILayout.Space(8);
        GUILayout.Label("Output Root Folder", EditorStyles.boldLabel);

        outputRootFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            outputRootFolder,
            typeof(DefaultAsset),
            false);

        GUILayout.Space(8);

        replaceSceneInstances = EditorGUILayout.ToggleLeft(
            "整理后替换场景中的对应预制体实例",
            replaceSceneInstances);

        GUILayout.Space(10);

        DrawSourceAssetCount();

        GUILayout.Space(10);

        assetTabIndex = GUILayout.Toolbar(assetTabIndex, AssetTabs);

        GUILayout.Space(8);

        if (assetTabIndex == 0)
        {
            DrawPrefabAssetList();
        }
        else
        {
            DrawLayerAssetList();
        }

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Fix Assets"))
        {
            Execute();
        }

        if (GUILayout.Button("修复资源绑定"))
        {
            ExecuteRepairBindings();
        }

        if (GUILayout.Button("Clear"))
        {
            ClearSelections();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void ClearSelections()
    {
        sourceTerrain = null;
        sourceSceneObject = null;
        outputRootFolder = null;
        replaceSceneInstances = false;
        assetTabIndex = 0;
        prefabSelections.Clear();
        prefabNameOverrides.Clear();
        layerSelections.Clear();
        layerNameOverrides.Clear();

        GUI.FocusControl(null);
        Repaint();
    }

    private void DrawSourceAssetCount()
    {
        int sourceAssetCount = GetSourceAssetCount();
        int selectedExportCount = GetSelectedExportCount();
        string message =
            $"源资产数量: {sourceAssetCount} | 已勾选导出项: {selectedExportCount}";

        if (sourceTerrain != null &&
            sourceTerrain.terrainData == null)
        {
            message += "  (TerrainData 无效)";
        }

        EditorGUILayout.HelpBox(message, MessageType.Info);
    }

    private void DrawPrefabAssetList()
    {
        List<string> prefabPaths = GetSourcePrefabPaths();

        SyncPrefabState(prefabPaths);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Prefab 资产列表 ({prefabPaths.Count})",
            EditorStyles.boldLabel);

        if (GUILayout.Button("全选", GUILayout.Width(60)))
        {
            SetSelections(prefabSelections, prefabPaths, true);
        }

        if (GUILayout.Button("全不选", GUILayout.Width(60)))
        {
            SetSelections(prefabSelections, prefabPaths, false);
        }

        EditorGUILayout.EndHorizontal();

        if (prefabPaths.Count == 0)
        {
            EditorGUILayout.HelpBox("当前没有可整理的 Prefab 资产", MessageType.None);
            return;
        }

        float listHeight = GetAdaptiveListHeight();

        prefabListScroll = EditorGUILayout.BeginScrollView(
            prefabListScroll,
            GUILayout.Height(listHeight));

        foreach (string prefabPath in prefabPaths)
        {
            string originalName =
                Path.GetFileNameWithoutExtension(prefabPath);

            prefabSelections[prefabPath] =
                EditorGUILayout.ToggleLeft(
                    originalName,
                    prefabSelections[prefabPath]);

            EditorGUILayout.LabelField(originalName, prefabPath);

            prefabNameOverrides[prefabPath] =
                EditorGUILayout.TextField(
                    "自定义命名",
                    prefabNameOverrides[prefabPath]);

            GUILayout.Space(6);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawLayerAssetList()
    {
        List<string> layerPaths = GetSourceLayerPaths();

        SyncLayerState(layerPaths);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Layer 资产列表 ({layerPaths.Count})",
            EditorStyles.boldLabel);

        if (GUILayout.Button("全选", GUILayout.Width(60)))
        {
            SetSelections(layerSelections, layerPaths, true);
        }

        if (GUILayout.Button("全不选", GUILayout.Width(60)))
        {
            SetSelections(layerSelections, layerPaths, false);
        }

        EditorGUILayout.EndHorizontal();

        if (layerPaths.Count == 0)
        {
            EditorGUILayout.HelpBox("当前没有可整理的 Layer 资产", MessageType.None);
            return;
        }

        float listHeight = GetAdaptiveListHeight();

        layerListScroll = EditorGUILayout.BeginScrollView(
            layerListScroll,
            GUILayout.Height(listHeight));

        foreach (string layerPath in layerPaths)
        {
            string originalName =
                Path.GetFileNameWithoutExtension(layerPath);

            layerSelections[layerPath] =
                EditorGUILayout.ToggleLeft(
                    originalName,
                    layerSelections[layerPath]);

            EditorGUILayout.LabelField(originalName, layerPath);

            layerNameOverrides[layerPath] =
                EditorGUILayout.TextField(
                    "自定义命名",
                    layerNameOverrides[layerPath]);

            GUILayout.Space(6);
        }

        EditorGUILayout.EndScrollView();
    }

    private float GetAdaptiveListHeight()
    {
        float byReserve =
            Mathf.Max(MinListHeight, position.height - ListBottomReserve);

        float byRatio =
            Mathf.Max(MinListHeight, position.height * MaxListHeightRatio);

        return Mathf.Min(byReserve, byRatio);
    }

    private int GetSourceAssetCount()
    {
        HashSet<string> sourceAssets =
            new HashSet<string>();

        if (sourceTerrain != null)
        {
            CollectTerrainSourceAssets(
                sourceTerrain,
                sourceAssets);
        }

        if (sourceSceneObject != null)
        {
            CollectSceneSourceAssets(
                sourceSceneObject,
                sourceAssets);
        }

        return sourceAssets.Count;
    }

    private int GetSelectedExportCount()
    {
        SyncSelectionStates();

        int count = 0;

        foreach (bool isSelected in prefabSelections.Values)
        {
            if (isSelected)
            {
                count++;
            }
        }

        foreach (bool isSelected in layerSelections.Values)
        {
            if (isSelected)
            {
                count++;
            }
        }

        return count;
    }

    private void SyncSelectionStates()
    {
        SyncPrefabState(GetSourcePrefabPaths());
        SyncLayerState(GetSourceLayerPaths());
    }

    private List<string> GetSourcePrefabPaths()
    {
        HashSet<string> prefabPaths =
            new HashSet<string>();

        if (sourceTerrain != null)
        {
            CollectTerrainPrefabPaths(
                sourceTerrain,
                prefabPaths);
        }

        if (sourceSceneObject != null)
        {
            CollectScenePrefabPaths(
                sourceSceneObject,
                prefabPaths);
        }

        List<string> result = new List<string>(prefabPaths);
        result.Sort();
        return result;
    }

    private List<string> GetSourceLayerPaths()
    {
        HashSet<string> layerPaths =
            new HashSet<string>();

        if (sourceTerrain != null)
        {
            CollectTerrainLayerPaths(
                sourceTerrain,
                layerPaths);
        }

        List<string> result = new List<string>(layerPaths);
        result.Sort();
        return result;
    }

    private void CollectTerrainPrefabPaths(
        Terrain terrain,
        HashSet<string> prefabPaths)
    {
        if (terrain.terrainData == null)
            return;

        TreePrototype[] treePrototypes =
            terrain.terrainData.treePrototypes;

        foreach (TreePrototype treePrototype in treePrototypes)
        {
            string prefabPath =
                GetPrefabPath(treePrototype);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                prefabPaths.Add(prefabPath);
            }
        }
    }

    private void CollectScenePrefabPaths(
        GameObject rootObject,
        HashSet<string> prefabPaths)
    {
        Transform[] transforms =
            rootObject.GetComponentsInChildren<Transform>(true);

        foreach (Transform item in transforms)
        {
            string prefabPath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                prefabPaths.Add(prefabPath);
            }
        }
    }

    private void CollectTerrainLayerPaths(
        Terrain terrain,
        HashSet<string> layerPaths)
    {
        if (terrain.terrainData == null)
            return;

        TerrainLayer[] terrainLayers =
            terrain.terrainData.terrainLayers;

        foreach (TerrainLayer terrainLayer in terrainLayers)
        {
            if (terrainLayer == null)
                continue;

            string layerPath =
                AssetDatabase.GetAssetPath(terrainLayer);

            if (!string.IsNullOrEmpty(layerPath))
            {
                layerPaths.Add(layerPath);
            }
        }
    }

    private void SyncPrefabState(List<string> prefabPaths)
    {
        HashSet<string> activePaths =
            new HashSet<string>(prefabPaths);

        List<string> stalePaths =
            new List<string>(prefabSelections.Keys);

        foreach (string stalePath in stalePaths)
        {
            if (!activePaths.Contains(stalePath))
            {
                prefabSelections.Remove(stalePath);
                prefabNameOverrides.Remove(stalePath);
            }
        }

        foreach (string prefabPath in prefabPaths)
        {
            if (!prefabSelections.ContainsKey(prefabPath))
            {
                prefabSelections[prefabPath] = true;
            }

            if (!prefabNameOverrides.ContainsKey(prefabPath))
            {
                prefabNameOverrides[prefabPath] = string.Empty;
            }
        }
    }

    private void SyncLayerState(List<string> layerPaths)
    {
        HashSet<string> activePaths =
            new HashSet<string>(layerPaths);

        List<string> stalePaths =
            new List<string>(layerSelections.Keys);

        foreach (string stalePath in stalePaths)
        {
            if (!activePaths.Contains(stalePath))
            {
                layerSelections.Remove(stalePath);
                layerNameOverrides.Remove(stalePath);
            }
        }

        foreach (string layerPath in layerPaths)
        {
            if (!layerSelections.ContainsKey(layerPath))
            {
                layerSelections[layerPath] = false;
            }

            if (!layerNameOverrides.ContainsKey(layerPath))
            {
                layerNameOverrides[layerPath] = string.Empty;
            }
        }
    }

    private void SetSelections(
        Dictionary<string, bool> selections,
        List<string> paths,
        bool isSelected)
    {
        foreach (string path in paths)
        {
            selections[path] = isSelected;
        }
    }

    private void CollectTerrainSourceAssets(
        Terrain terrain,
        HashSet<string> sourceAssets)
    {
        if (terrain.terrainData == null)
            return;

        TreePrototype[] treePrototypes =
            terrain.terrainData.treePrototypes;

        foreach (TreePrototype treePrototype in treePrototypes)
        {
            string prefabPath =
                GetPrefabPath(treePrototype);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                sourceAssets.Add(prefabPath);
            }
        }

        TerrainLayer[] terrainLayers =
            terrain.terrainData.terrainLayers;

        foreach (TerrainLayer terrainLayer in terrainLayers)
        {
            if (terrainLayer == null)
                continue;

            string layerPath =
                AssetDatabase.GetAssetPath(terrainLayer);

            if (!string.IsNullOrEmpty(layerPath))
            {
                sourceAssets.Add(layerPath);
            }
        }
    }

    private void CollectSceneSourceAssets(
        GameObject rootObject,
        HashSet<string> sourceAssets)
    {
        Transform[] transforms =
            rootObject.GetComponentsInChildren<Transform>(true);

        foreach (Transform item in transforms)
        {
            GameObject sceneObject = item.gameObject;
            string sourcePath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneObject);

            string uniqueKey =
                GetProcessKey(sceneObject, sourcePath);

            if (!string.IsNullOrEmpty(uniqueKey))
            {
                sourceAssets.Add(uniqueKey);
            }
        }
    }

    private void Execute()
    {
        if (sourceTerrain == null &&
            sourceSceneObject == null)
        {
            Debug.LogError("请至少选择一个 Terrain 或场景物体");
            return;
        }

        if (sourceTerrain != null &&
            sourceTerrain.terrainData == null)
        {
            Debug.LogError("所选 Terrain 缺少 TerrainData");
            return;
        }

        string outputRootPath = GetOutputRootPath();

        if (string.IsNullOrEmpty(outputRootPath))
        {
            return;
        }

        SyncSelectionStates();

        if (GetSelectedExportCount() == 0)
        {
            Debug.LogWarning("当前没有勾选任何导出项");
            return;
        }

        HashSet<string> processedAssets =
            new HashSet<string>();

        Dictionary<string, string> exportedPrefabMap =
            new Dictionary<string, string>();

        Dictionary<string, string> exportedLayerMap =
            new Dictionary<string, string>();

        int processedCount = 0;

        if (sourceTerrain != null)
        {
            processedCount += ProcessTerrainPrefabs(
                sourceTerrain,
                outputRootPath,
                processedAssets,
                exportedPrefabMap);

            processedCount += ProcessTerrainLayers(
                sourceTerrain,
                outputRootPath,
                processedAssets,
                exportedLayerMap);
        }

        if (sourceSceneObject != null)
        {
            processedCount += ProcessSceneObjects(
                sourceSceneObject,
                outputRootPath,
                processedAssets,
                exportedPrefabMap);
        }

        int replacedCount = 0;

        if (replaceSceneInstances &&
            (sourceSceneObject != null || sourceTerrain != null))
        {
            if (sourceTerrain != null)
            {
                CollectTerrainReplacePrefabMap(
                    sourceTerrain,
                    outputRootPath,
                    exportedPrefabMap);

                CollectTerrainReplaceLayerMap(
                    sourceTerrain,
                    outputRootPath,
                    exportedLayerMap);
            }

            if (sourceSceneObject != null)
            {
                CollectReplacePrefabMap(
                    sourceSceneObject,
                    outputRootPath,
                    exportedPrefabMap);
            }

            if (exportedPrefabMap.Count == 0)
            {
                Debug.LogWarning("未找到可用于替换的导出预制体");
            }

            if (sourceSceneObject != null)
            {
                replacedCount += ReplaceSceneInstances(
                    sourceSceneObject,
                    exportedPrefabMap);
            }

            if (sourceTerrain != null)
            {
                replacedCount += ReplaceTerrainOnCopiedObject(
                    sourceTerrain,
                    outputRootPath,
                    exportedPrefabMap,
                    exportedLayerMap);
            }
        }

        if (processedCount == 0)
        {
            Debug.LogWarning("没有找到可整理的唯一物体或 Prefab 资源");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"整理完成，导出项: {processedCount}，替换实例: {replacedCount}");
    }

    private void ExecuteRepairBindings()
    {
        if (sourceTerrain == null &&
            sourceSceneObject == null)
        {
            Debug.LogError("请至少选择一个 Terrain 或场景物体");
            return;
        }

        if (sourceTerrain != null &&
            sourceTerrain.terrainData == null)
        {
            Debug.LogError("所选 Terrain 缺少 TerrainData");
            return;
        }

        string outputRootPath = GetOutputRootPath();

        if (string.IsNullOrEmpty(outputRootPath))
        {
            return;
        }

        SyncSelectionStates();

        List<string> prefabPaths = GetSourcePrefabPaths();
        int selectedPrefabCount = 0;

        foreach (string prefabPath in prefabPaths)
        {
            if (IsPrefabSelected(prefabPath))
            {
                selectedPrefabCount++;
            }
        }

        if (selectedPrefabCount == 0)
        {
            Debug.LogWarning("当前没有勾选任何 Prefab 导出项");
            return;
        }

        HashSet<string> processedAssets =
            new HashSet<string>();

        int repairedCount = 0;

        if (sourceTerrain != null)
        {
            repairedCount += RepairTerrainPrefabBindings(
                sourceTerrain,
                outputRootPath,
                processedAssets);
        }

        if (sourceSceneObject != null)
        {
            repairedCount += RepairScenePrefabBindings(
                sourceSceneObject,
                outputRootPath,
                processedAssets);
        }

        if (repairedCount == 0)
        {
            Debug.LogWarning("没有找到可修复绑定的导出预制体");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"修复资源绑定完成，已处理预制体: {repairedCount}");
    }

    private int RepairTerrainPrefabBindings(
        Terrain terrain,
        string outputRootPath,
        HashSet<string> processedAssets)
    {
        TreePrototype[] treePrototypes =
            terrain.terrainData.treePrototypes;

        int repairedCount = 0;

        foreach (TreePrototype treePrototype in treePrototypes)
        {
            string prefabPath =
                GetPrefabPath(treePrototype);

            if (string.IsNullOrEmpty(prefabPath) ||
                !IsPrefabSelected(prefabPath) ||
                !processedAssets.Add(prefabPath))
            {
                continue;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
                continue;

            ProcessObject(prefab, prefabPath, outputRootPath);
            repairedCount++;
        }

        return repairedCount;
    }

    private int RepairScenePrefabBindings(
        GameObject rootObject,
        string outputRootPath,
        HashSet<string> processedAssets)
    {
        int repairedCount = 0;

        Transform[] transforms =
            rootObject.GetComponentsInChildren<Transform>(true);

        foreach (Transform item in transforms)
        {
            GameObject sceneObject =
                PrefabUtility.GetNearestPrefabInstanceRoot(item.gameObject);

            if (sceneObject == null)
            {
                continue;
            }

            string sourcePath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneObject);

            if (string.IsNullOrEmpty(sourcePath) ||
                !IsPrefabSelected(sourcePath))
            {
                continue;
            }

            string uniqueKey =
                GetProcessKey(sceneObject, sourcePath);

            if (string.IsNullOrEmpty(uniqueKey) ||
                !processedAssets.Add(uniqueKey))
            {
                continue;
            }

            ProcessObject(sceneObject, sourcePath, outputRootPath);
            repairedCount++;
        }

        return repairedCount;
    }

    private int ProcessTerrainPrefabs(
        Terrain terrain,
        string outputRootPath,
        HashSet<string> processedAssets,
        Dictionary<string, string> exportedPrefabMap)
    {
        TreePrototype[] treePrototypes =
            terrain.terrainData.treePrototypes;

        if (treePrototypes == null ||
            treePrototypes.Length == 0)
        {
            Debug.LogWarning("所选 Terrain 没有树木原型可提取");
            return 0;
        }

        int processedCount = 0;

        foreach (TreePrototype treePrototype in treePrototypes)
        {
            string prefabPath =
                GetPrefabPath(treePrototype);

            if (string.IsNullOrEmpty(prefabPath) ||
                !IsPrefabSelected(prefabPath))
            {
                continue;
            }

            string exportedPrefabPath =
                GetExportedPrefabPath(null, prefabPath, outputRootPath);

            if (!string.IsNullOrEmpty(exportedPrefabPath) &&
                File.Exists(exportedPrefabPath))
            {
                exportedPrefabMap[prefabPath] = exportedPrefabPath;
            }

            if (!processedAssets.Add(prefabPath))
            {
                continue;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
                continue;

            ProcessObject(prefab, prefabPath, outputRootPath);

            if (!string.IsNullOrEmpty(exportedPrefabPath) &&
                File.Exists(exportedPrefabPath))
            {
                exportedPrefabMap[prefabPath] = exportedPrefabPath;
            }

            processedCount++;
        }

        return processedCount;
    }

    private int ProcessTerrainLayers(
        Terrain terrain,
        string outputRootPath,
        HashSet<string> processedAssets,
        Dictionary<string, string> exportedLayerMap)
    {
        TerrainLayer[] terrainLayers =
            terrain.terrainData.terrainLayers;

        if (terrainLayers == null ||
            terrainLayers.Length == 0)
        {
            return 0;
        }

        string layerDir =
            $"{outputRootPath}/Layer";

        CreateFolder(layerDir);

        int processedCount = 0;

        foreach (TerrainLayer terrainLayer in terrainLayers)
        {
            if (terrainLayer == null)
                continue;

            string layerPath =
                AssetDatabase.GetAssetPath(terrainLayer);

            string exportedLayerPath =
                GetExportedLayerPath(terrainLayer, layerDir);

            if (!string.IsNullOrEmpty(layerPath) &&
                !string.IsNullOrEmpty(exportedLayerPath) &&
                File.Exists(exportedLayerPath))
            {
                exportedLayerMap[layerPath] = exportedLayerPath;
            }

            if (!IsLayerSelected(layerPath))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(layerPath) &&
                !processedAssets.Add(layerPath))
            {
                continue;
            }

            CopyTerrainLayer(terrainLayer, layerDir);

            if (!string.IsNullOrEmpty(layerPath) &&
                !string.IsNullOrEmpty(exportedLayerPath) &&
                File.Exists(exportedLayerPath))
            {
                exportedLayerMap[layerPath] = exportedLayerPath;
            }

            processedCount++;
        }

        return processedCount;
    }

    private int ProcessSceneObjects(
        GameObject rootObject,
        string outputRootPath,
        HashSet<string> processedAssets,
        Dictionary<string, string> exportedPrefabMap)
    {
        int processedCount = 0;

        Transform[] transforms =
            rootObject.GetComponentsInChildren<Transform>(true);

        foreach (Transform item in transforms)
        {
            GameObject sceneObject =
                PrefabUtility.GetNearestPrefabInstanceRoot(item.gameObject);

            if (sceneObject == null)
            {
                continue;
            }

            string sourcePath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneObject);

            if (string.IsNullOrEmpty(sourcePath))
            {
                continue;
            }

            string uniqueKey =
                GetProcessKey(sceneObject, sourcePath);

            if (string.IsNullOrEmpty(uniqueKey) ||
                !IsPrefabSelected(sourcePath))
            {
                continue;
            }

            string exportedPrefabPath =
                GetExportedPrefabPath(sceneObject, sourcePath, outputRootPath);

            if (!string.IsNullOrEmpty(exportedPrefabPath) &&
                File.Exists(exportedPrefabPath))
            {
                exportedPrefabMap[sourcePath] = exportedPrefabPath;
            }

            if (!processedAssets.Add(uniqueKey))
            {
                continue;
            }

            ProcessObject(sceneObject, sourcePath, outputRootPath);

            if (!string.IsNullOrEmpty(exportedPrefabPath) &&
                File.Exists(exportedPrefabPath))
            {
                exportedPrefabMap[sourcePath] = exportedPrefabPath;
            }

            processedCount++;
        }

        return processedCount;
    }

    private int ReplaceSceneInstances(
        GameObject rootObject,
        Dictionary<string, string> exportedPrefabMap)
    {
        int replacedCount = 0;

        GameObject rootBackup =
            CreateRootBackup(rootObject);

        if (rootBackup == null)
        {
            Debug.LogWarning("源节点整体备份失败，已取消替换以避免源节点丢失");
            return 0;
        }

        Transform[] transforms =
            rootObject.GetComponentsInChildren<Transform>(true);

        List<GameObject> instanceRoots =
            new List<GameObject>();

        HashSet<GameObject> uniqueRoots =
            new HashSet<GameObject>();

        foreach (Transform item in transforms)
        {
            if (item == null)
                continue;

            GameObject instanceRoot =
                PrefabUtility.GetNearestPrefabInstanceRoot(item.gameObject);

            if (instanceRoot == null)
                continue;

            if (uniqueRoots.Add(instanceRoot))
            {
                instanceRoots.Add(instanceRoot);
            }
        }

        foreach (GameObject instanceRoot in instanceRoots)
        {
            if (instanceRoot == null)
            {
                continue;
            }

            string sourcePath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);

            if (string.IsNullOrEmpty(sourcePath) ||
                !exportedPrefabMap.TryGetValue(sourcePath, out string prefabPath))
            {
                continue;
            }

            GameObject exportedPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (exportedPrefab == null)
                continue;

            Transform src = instanceRoot.transform;
            Transform parent = src.parent;
            int siblingIndex = src.GetSiblingIndex();
            bool wasActive = instanceRoot.activeSelf;
            string oldName = instanceRoot.name;

            GameObject newInstance =
                (GameObject)PrefabUtility.InstantiatePrefab(
                    exportedPrefab,
                    instanceRoot.scene);

            if (newInstance == null)
                continue;

            Undo.RegisterCreatedObjectUndo(newInstance, "Replace Scene Prefab");

            Transform dst = newInstance.transform;
            dst.SetParent(parent, true);
            dst.SetSiblingIndex(siblingIndex);
            dst.position = src.position;
            dst.rotation = src.rotation;
            dst.localScale = src.localScale;
            newInstance.name = oldName;
            newInstance.SetActive(wasActive);

            Undo.DestroyObjectImmediate(instanceRoot);
            replacedCount++;
        }

        return replacedCount;
    }

    private GameObject CreateRootBackup(GameObject sourceRoot)
    {
        if (sourceRoot == null)
            return null;

        Transform src = sourceRoot.transform;
        Transform parent = src.parent;
        int siblingIndex = src.GetSiblingIndex();

        GameObject backup =
            Object.Instantiate(sourceRoot, parent);

        if (backup == null)
            return null;

        Undo.RegisterCreatedObjectUndo(backup, "Backup Source Root");

        backup.name =
            GetUniqueBackupName(sourceRoot, sourceRoot.name + "_SourceBackup");
        backup.transform.SetSiblingIndex(siblingIndex);
        backup.SetActive(false);

        return backup;
    }

    private void CollectReplacePrefabMap(
        GameObject rootObject,
        string outputRootPath,
        Dictionary<string, string> exportedPrefabMap)
    {
        Transform[] transforms =
            rootObject.GetComponentsInChildren<Transform>(true);

        foreach (Transform item in transforms)
        {
            string sourcePath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject);

            if (string.IsNullOrEmpty(sourcePath) ||
                !IsPrefabSelected(sourcePath))
            {
                continue;
            }

            string exportedPrefabPath =
                GetExportedPrefabPath(item.gameObject, sourcePath, outputRootPath);

            if (!string.IsNullOrEmpty(exportedPrefabPath) &&
                File.Exists(exportedPrefabPath))
            {
                exportedPrefabMap[sourcePath] = exportedPrefabPath;
            }
        }
    }

    private void CollectTerrainReplacePrefabMap(
        Terrain terrain,
        string outputRootPath,
        Dictionary<string, string> exportedPrefabMap)
    {
        TreePrototype[] treePrototypes =
            terrain.terrainData.treePrototypes;

        foreach (TreePrototype treePrototype in treePrototypes)
        {
            string sourcePath =
                GetPrefabPath(treePrototype);

            if (string.IsNullOrEmpty(sourcePath) ||
                !IsPrefabSelected(sourcePath))
            {
                continue;
            }

            string exportedPrefabPath =
                GetExportedPrefabPath(null, sourcePath, outputRootPath);

            if (!string.IsNullOrEmpty(exportedPrefabPath) &&
                File.Exists(exportedPrefabPath))
            {
                exportedPrefabMap[sourcePath] = exportedPrefabPath;
            }
        }
    }

    private void CollectTerrainReplaceLayerMap(
        Terrain terrain,
        string outputRootPath,
        Dictionary<string, string> exportedLayerMap)
    {
        TerrainLayer[] terrainLayers =
            terrain.terrainData.terrainLayers;

        string layerDir =
            $"{outputRootPath}/Layer";

        foreach (TerrainLayer terrainLayer in terrainLayers)
        {
            if (terrainLayer == null)
                continue;

            string sourceLayerPath =
                AssetDatabase.GetAssetPath(terrainLayer);

            if (string.IsNullOrEmpty(sourceLayerPath) ||
                !IsLayerSelected(sourceLayerPath))
            {
                continue;
            }

            string exportedLayerPath =
                GetExportedLayerPath(terrainLayer, layerDir);

            if (!string.IsNullOrEmpty(exportedLayerPath) &&
                File.Exists(exportedLayerPath))
            {
                exportedLayerMap[sourceLayerPath] = exportedLayerPath;
            }
        }
    }

    private int ReplaceTerrainOnCopiedObject(
        Terrain terrain,
        string outputRootPath,
        Dictionary<string, string> exportedPrefabMap,
        Dictionary<string, string> exportedLayerMap)
    {
        Terrain copiedTerrain =
            CreateTerrainObjectCopy(terrain);

        if (copiedTerrain == null)
        {
            Debug.LogWarning("地形对象复制失败，已跳过地形替换");
            return 0;
        }

        TerrainData sourceTerrainData = terrain.terrainData;
        TerrainData terrainData =
            CreateTerrainDataBackup(copiedTerrain, sourceTerrainData, outputRootPath);

        if (terrainData == null)
        {
            Debug.LogWarning("地形数据备份失败，已跳过地形替换");
            return 0;
        }

        copiedTerrain.terrainData = terrainData;

        TerrainCollider copiedCollider =
            copiedTerrain.GetComponent<TerrainCollider>();

        if (copiedCollider != null)
        {
            copiedCollider.terrainData = terrainData;
        }

        int replacedCount = 0;
        bool changed = false;

        Undo.RecordObject(terrainData, "Replace Terrain Tree Prototypes");
        Undo.RecordObject(copiedTerrain, "Replace Terrain Data On Copy");

        replacedCount += ReplaceTerrainTreePrototypesInData(
            terrainData,
            exportedPrefabMap,
            ref changed);

        replacedCount += ReplaceTerrainLayersInData(
            terrainData,
            exportedLayerMap,
            ref changed);

        if (changed)
        {
            EditorUtility.SetDirty(terrainData);
            EditorUtility.SetDirty(copiedTerrain);
        }

        return replacedCount;
    }

    private int ReplaceTerrainTreePrototypesInData(
        TerrainData terrainData,
        Dictionary<string, string> exportedPrefabMap,
        ref bool changed)
    {
        TreePrototype[] treePrototypes = terrainData.treePrototypes;
        int replacedCount = 0;

        for (int i = 0; i < treePrototypes.Length; i++)
        {
            TreePrototype prototype = treePrototypes[i];

            if (prototype.prefab == null)
                continue;

            string sourcePath =
                AssetDatabase.GetAssetPath(prototype.prefab);

            if (string.IsNullOrEmpty(sourcePath) ||
                !exportedPrefabMap.TryGetValue(sourcePath, out string prefabPath))
            {
                continue;
            }

            GameObject exportedPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (exportedPrefab == null ||
                exportedPrefab == prototype.prefab)
            {
                continue;
            }

            prototype.prefab = exportedPrefab;
            treePrototypes[i] = prototype;
            changed = true;
            replacedCount++;
        }

        if (changed)
        {
            terrainData.treePrototypes = treePrototypes;
        }

        return replacedCount;
    }

    private int ReplaceTerrainLayersInData(
        TerrainData terrainData,
        Dictionary<string, string> exportedLayerMap,
        ref bool changed)
    {
        TerrainLayer[] terrainLayers = terrainData.terrainLayers;
        int replacedCount = 0;

        for (int i = 0; i < terrainLayers.Length; i++)
        {
            TerrainLayer sourceLayer = terrainLayers[i];

            if (sourceLayer == null)
                continue;

            string sourceLayerPath =
                AssetDatabase.GetAssetPath(sourceLayer);

            if (string.IsNullOrEmpty(sourceLayerPath) ||
                !exportedLayerMap.TryGetValue(sourceLayerPath, out string layerPath))
            {
                continue;
            }

            TerrainLayer exportedLayer =
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);

            if (exportedLayer == null ||
                exportedLayer == sourceLayer)
            {
                continue;
            }

            terrainLayers[i] = exportedLayer;
            changed = true;
            replacedCount++;
        }

        if (changed)
        {
            terrainData.terrainLayers = terrainLayers;
        }

        return replacedCount;
    }

    private Terrain CreateTerrainObjectCopy(Terrain sourceTerrain)
    {
        if (sourceTerrain == null)
            return null;

        Transform src = sourceTerrain.transform;
        Transform parent = src.parent;
        int siblingIndex = src.GetSiblingIndex();

        GameObject terrainCopy =
            Object.Instantiate(sourceTerrain.gameObject);

        if (terrainCopy == null)
            return null;

        Undo.RegisterCreatedObjectUndo(terrainCopy, "Create Terrain Copy");

        Terrain copiedTerrain =
            terrainCopy.GetComponent<Terrain>();

        if (copiedTerrain == null)
        {
            Object.DestroyImmediate(terrainCopy);
            return null;
        }

        terrainCopy.transform.position = src.position;
        terrainCopy.transform.rotation = src.rotation;
        terrainCopy.transform.localScale = src.localScale;

        if (parent != null)
        {
            terrainCopy.transform.SetParent(parent, true);
            terrainCopy.transform.SetSiblingIndex(siblingIndex + 1);
        }
        else
        {
            terrainCopy.transform.SetSiblingIndex(siblingIndex + 1);
        }

        terrainCopy.name =
            GetUniqueBackupName(sourceTerrain.gameObject, sourceTerrain.name + "_Organized");

        return copiedTerrain;
    }

    private TerrainData CreateTerrainDataBackup(
        Terrain terrain,
        TerrainData sourceTerrainData,
        string outputRootPath)
    {
        if (terrain == null ||
            sourceTerrainData == null)
        {
            return null;
        }

        string terrainDataDir =
            $"{outputRootPath}/TerrainData";

        CreateFolder(terrainDataDir);

        string backupName =
            SanitizeFileName(terrain.name + "_TerrainDataBackup");

        if (string.IsNullOrEmpty(backupName))
        {
            backupName = "TerrainDataBackup";
        }

        string backupPath =
            $"{terrainDataDir}/{backupName}.asset";

        TerrainData backupTerrainData =
            AssetDatabase.LoadAssetAtPath<TerrainData>(backupPath);

        if (backupTerrainData != null)
        {
            return backupTerrainData;
        }

        string sourcePath =
            AssetDatabase.GetAssetPath(sourceTerrainData);

        if (!string.IsNullOrEmpty(sourcePath) &&
            File.Exists(AssetDatabase.GetAssetPath(sourceTerrainData)))
        {
            if (!File.Exists(backupPath))
            {
                AssetDatabase.CopyAsset(sourcePath, backupPath);
            }

            backupTerrainData =
                AssetDatabase.LoadAssetAtPath<TerrainData>(backupPath);

            if (backupTerrainData != null)
            {
                return backupTerrainData;
            }
        }

        backupTerrainData =
            Object.Instantiate(sourceTerrainData);

        if (backupTerrainData == null)
        {
            return null;
        }

        AssetDatabase.CreateAsset(backupTerrainData, backupPath);
        return backupTerrainData;
    }

    private string GetUniqueBackupName(GameObject sourceRoot, string baseName)
    {
        HashSet<string> siblingNames =
            new HashSet<string>();

        Transform parent = sourceRoot.transform.parent;

        if (parent != null)
        {
            foreach (Transform child in parent)
            {
                siblingNames.Add(child.name);
            }
        }
        else
        {
            GameObject[] roots = sourceRoot.scene.GetRootGameObjects();

            foreach (GameObject root in roots)
            {
                siblingNames.Add(root.name);
            }
        }

        if (!siblingNames.Contains(baseName))
        {
            return baseName;
        }

        int index = 1;

        while (siblingNames.Contains($"{baseName}_{index}"))
        {
            index++;
        }

        return $"{baseName}_{index}";
    }

    private string GetExportedPrefabPath(
        GameObject sourceObject,
        string sourcePath,
        string outputRootPath)
    {
        string exportName =
            GetObjectExportName(sourceObject, sourcePath);

        return $"{outputRootPath}/Prefab/{exportName}/{exportName}.prefab";
    }

    private string GetPrefabPath(TreePrototype treePrototype)
    {
        if (treePrototype == null ||
            treePrototype.prefab == null)
        {
            return null;
        }

        return AssetDatabase.GetAssetPath(treePrototype.prefab);
    }

    private string GetProcessKey(GameObject sceneObject, string sourcePath)
    {
        if (!string.IsNullOrEmpty(sourcePath))
        {
            return sourcePath;
        }

        GlobalObjectId globalId =
            GlobalObjectId.GetGlobalObjectIdSlow(sceneObject);

        return globalId.ToString();
    }

    private void ProcessObject(
        GameObject sourceObject,
        string sourcePath,
        string outputRootPath)
    {
        string treeName =
            GetObjectExportName(sourceObject, sourcePath);

        string modelDir =
            $"{outputRootPath}/Model/{treeName}";

        string prefabDir =
            $"{outputRootPath}/Prefab/{treeName}";

        string matDir =
            $"{outputRootPath}/Materials/{treeName}";

        string texDir =
            $"{outputRootPath}/Texture/{treeName}";

        CreateFolder(modelDir);
        CreateFolder(prefabDir);
        CreateFolder(matDir);
        CreateFolder(texDir);

        Dictionary<string, Material> matMap =
            new Dictionary<string, Material>();

        Dictionary<string, string> materialSuffixMap =
            new Dictionary<string, string>();

        Dictionary<string, int> materialSuffixCountMap =
            new Dictionary<string, int>();

        Dictionary<string, int> materialSuffixIndexMap =
            new Dictionary<string, int>();

        Dictionary<string, Mesh> exportedMeshMap =
            new Dictionary<string, Mesh>();

        string[] deps =
            GetDependencies(sourceObject, sourcePath);

        bool copiedModelFile = false;

        foreach (string dep in deps)
        {
            if (dep == sourcePath)
                continue;

            string ext =
                Path.GetExtension(dep).ToLower();

            if (IsModelFileExtension(ext))
            {
                CopyModel(dep, treeName, modelDir, exportedMeshMap);
                copiedModelFile = true;
            }
        }

        if (!copiedModelFile)
        {
            CopyMeshAssetsFallback(sourceObject, treeName, modelDir, exportedMeshMap);
        }

        Renderer[] sourceRenderers =
            sourceObject.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer sourceRenderer in sourceRenderers)
        {
            if (sourceRenderer == null)
                continue;

            Material[] sourceMaterials =
                sourceRenderer.sharedMaterials;

            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                Material srcMat = sourceMaterials[i];

                if (srcMat == null)
                    continue;

                string materialKey =
                    AssetDatabase.GetAssetPath(srcMat);

                if (string.IsNullOrEmpty(materialKey))
                {
                    materialKey = srcMat.name + "|" + sourceRenderer.name + "|" + i;
                }

                if (!materialSuffixMap.ContainsKey(materialKey))
                {
                    string suffix =
                        GetMaterialType(srcMat.name);

                    materialSuffixMap[materialKey] = suffix;

                    if (!materialSuffixCountMap.ContainsKey(suffix))
                    {
                        materialSuffixCountMap[suffix] = 0;
                    }

                    materialSuffixCountMap[suffix]++;
                }

                if (matMap.ContainsKey(materialKey))
                    continue;

                string materialTag =
                    GetMaterialExportTag(
                        materialKey,
                        materialSuffixMap[materialKey],
                        materialSuffixCountMap,
                        materialSuffixIndexMap);

                Material dstMat =
                    CopyMaterial(
                        srcMat,
                        treeName,
                        matDir,
                        texDir,
                        materialTag);

                matMap[materialKey] = dstMat;
            }
        }

        GameObject prefab =
            CreateOutputPrefab(
                sourceObject,
                sourcePath,
                prefabDir,
                treeName);

        if (prefab == null)
        {
            return;
        }

        Renderer[] targetRenderers =
            prefab.GetComponentsInChildren<Renderer>(true);

        Dictionary<string, Renderer> targetRenderersByPath =
            BuildRendererPathMap(prefab);

        foreach (Renderer sourceRenderer in sourceRenderers)
        {
            if (sourceRenderer == null)
                continue;

            string rendererPath =
                GetRelativeTransformPath(sourceObject.transform, sourceRenderer.transform);

            if (!targetRenderersByPath.TryGetValue(rendererPath, out Renderer targetRenderer))
                continue;

            Material[] sourceMaterials =
                sourceRenderer.sharedMaterials;

            Material[] targetMaterials =
                targetRenderer.sharedMaterials;

            for (int i = 0; i < sourceMaterials.Length && i < targetMaterials.Length; i++)
            {
                Material srcMat = sourceMaterials[i];

                if (srcMat == null)
                    continue;

                string materialKey =
                    AssetDatabase.GetAssetPath(srcMat);

                if (string.IsNullOrEmpty(materialKey))
                {
                    materialKey = srcMat.name + "|" + sourceRenderer.name + "|" + i;
                }

                if (matMap.TryGetValue(materialKey, out Material dstMat) &&
                    dstMat != null)
                {
                    targetMaterials[i] = dstMat;
                }
            }

            targetRenderer.sharedMaterials = targetMaterials;
        }

        RemapExportedMeshes(sourceObject, prefab, exportedMeshMap);

        PrefabUtility.SavePrefabAsset(prefab);
    }

    private string GetObjectExportName(
        GameObject sourceObject,
        string sourcePath)
    {
        if (!string.IsNullOrEmpty(sourcePath))
        {
            string overrideName = GetPrefabOverrideName(sourcePath);

            if (!string.IsNullOrEmpty(overrideName))
            {
                return overrideName;
            }

            return Path.GetFileNameWithoutExtension(sourcePath);
        }

        return sourceObject.name;
    }

    private Dictionary<string, Renderer> BuildRendererPathMap(GameObject rootObject)
    {
        Dictionary<string, Renderer> rendererPathMap =
            new Dictionary<string, Renderer>();

        Renderer[] renderers =
            rootObject.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            string path =
                GetRelativeTransformPath(rootObject.transform, renderer.transform);

            if (!rendererPathMap.ContainsKey(path))
            {
                rendererPathMap[path] = renderer;
            }
        }

        return rendererPathMap;
    }


    private string GetMaterialExportTag(
        string materialKey,
        string suffix,
        Dictionary<string, int> materialSuffixCountMap,
        Dictionary<string, int> materialSuffixIndexMap)
    {
        if (materialSuffixCountMap == null || materialSuffixIndexMap == null)
        {
            return string.Empty;
        }

        if (!materialSuffixCountMap.TryGetValue(suffix, out int suffixCount) ||
            suffixCount <= 1)
        {
            return string.Empty;
        }

        if (materialSuffixIndexMap.TryGetValue(materialKey, out int existingIndex))
        {
            return existingIndex.ToString("D2");
        }

        int nextIndex =
            materialSuffixIndexMap.Count + 1;

        materialSuffixIndexMap[materialKey] = nextIndex;
        return nextIndex.ToString("D2");
    }

    private string GetPrefabOverrideName(string sourcePath)
    {
        if (!prefabNameOverrides.TryGetValue(sourcePath, out string overrideName))
        {
            return null;
        }

        string sanitizedName = SanitizeFileName(overrideName);
        return string.IsNullOrEmpty(sanitizedName) ? null : sanitizedName;
    }

    private bool IsPrefabSelected(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return true;

        if (!prefabSelections.TryGetValue(sourcePath, out bool isSelected))
        {
            return true;
        }

        return isSelected;
    }

    private bool IsLayerSelected(string layerPath)
    {
        if (string.IsNullOrEmpty(layerPath))
            return true;

        if (!layerSelections.TryGetValue(layerPath, out bool isSelected))
        {
            return true;
        }

        return isSelected;
    }

    private string[] GetDependencies(
        GameObject sourceObject,
        string sourcePath)
    {
        if (!string.IsNullOrEmpty(sourcePath))
        {
            return AssetDatabase.GetDependencies(sourcePath);
        }

        HashSet<string> dependencies =
            new HashSet<string>();

        Renderer[] renderers =
            sourceObject.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                AddAssetPath(material, dependencies);
            }
        }

        MeshFilter[] meshFilters =
            sourceObject.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            AddAssetPath(meshFilter.sharedMesh, dependencies);
        }

        SkinnedMeshRenderer[] skinnedMeshes =
            sourceObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (SkinnedMeshRenderer skinnedMesh in skinnedMeshes)
        {
            AddAssetPath(skinnedMesh.sharedMesh, dependencies);
        }

        string[] result = new string[dependencies.Count];
        dependencies.CopyTo(result);
        return result;
    }

    private GameObject CreateOutputPrefab(
        GameObject sourceObject,
        string sourcePath,
        string prefabDir,
        string treeName)
    {
        string prefabDst =
            $"{prefabDir}/{treeName}.prefab";

        if (!string.IsNullOrEmpty(sourcePath) &&
            Path.GetExtension(sourcePath).ToLower() == ".prefab")
        {
            if (!File.Exists(prefabDst))
            {
                AssetDatabase.CopyAsset(sourcePath, prefabDst);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabDst);
        }

        GameObject existingPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabDst);

        if (existingPrefab != null)
        {
            return existingPrefab;
        }

        GameObject tempRoot =
            Object.Instantiate(sourceObject);

        if (tempRoot == null)
        {
            return null;
        }

        tempRoot.name = treeName;
        tempRoot.transform.SetParent(null);

        SyncLodGroups(sourceObject, tempRoot);

        bool saveSuccess;
        PrefabUtility.SaveAsPrefabAsset(tempRoot, prefabDst, out saveSuccess);
        Object.DestroyImmediate(tempRoot);

        if (!saveSuccess)
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabDst);
    }

    private void SyncLodGroups(GameObject sourceRoot, GameObject targetRoot)
    {
        if (sourceRoot == null || targetRoot == null)
            return;

        LODGroup[] sourceGroups =
            sourceRoot.GetComponentsInChildren<LODGroup>(true);

        foreach (LODGroup sourceGroup in sourceGroups)
        {
            if (sourceGroup == null)
                continue;

            string groupPath =
                GetRelativeTransformPath(sourceRoot.transform, sourceGroup.transform);

            Transform targetTransform =
                FindRelativeTransform(targetRoot.transform, groupPath);

            if (targetTransform == null)
                continue;

            LODGroup targetGroup =
                targetTransform.GetComponent<LODGroup>();

            if (targetGroup == null)
                continue;

            targetGroup.fadeMode = sourceGroup.fadeMode;
            targetGroup.animateCrossFading = sourceGroup.animateCrossFading;
            LODGroup.crossFadeAnimationDuration =
                LODGroup.crossFadeAnimationDuration;
            targetGroup.size = sourceGroup.size;
            targetGroup.localReferencePoint = sourceGroup.localReferencePoint;

            LOD[] sourceLods = sourceGroup.GetLODs();
            LOD[] mappedLods = new LOD[sourceLods.Length];

            for (int i = 0; i < sourceLods.Length; i++)
            {
                Renderer[] sourceRenderers = sourceLods[i].renderers;
                Renderer[] mappedRenderers = new Renderer[sourceRenderers.Length];

                for (int j = 0; j < sourceRenderers.Length; j++)
                {
                    Renderer sourceRenderer = sourceRenderers[j];

                    if (sourceRenderer == null)
                        continue;

                    string rendererPath =
                        GetRelativeTransformPath(sourceRoot.transform, sourceRenderer.transform);

                    Transform mappedTransform =
                        FindRelativeTransform(targetRoot.transform, rendererPath);

                    if (mappedTransform == null)
                        continue;

                    mappedRenderers[j] = mappedTransform.GetComponent<Renderer>();
                }

                mappedLods[i] = sourceLods[i];
                mappedLods[i].renderers = mappedRenderers;
            }

            targetGroup.SetLODs(mappedLods);
            targetGroup.RecalculateBounds();
            EditorUtility.SetDirty(targetGroup);
        }
    }

    private string GetRelativeTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return string.Empty;

        if (root == target)
            return string.Empty;

        List<string> parts = new List<string>();
        Transform current = target;

        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        if (current == null)
            return string.Empty;

        parts.Reverse();
        return string.Join("/", parts);
    }

    private Transform FindRelativeTransform(Transform root, string relativePath)
    {
        if (root == null)
            return null;

        if (string.IsNullOrEmpty(relativePath))
            return root;

        string[] parts = relativePath.Split('/');
        Transform current = root;

        foreach (string part in parts)
        {
            if (current == null)
                return null;

            current = current.Find(part);
        }

        return current;
    }

    private void AddAssetPath(
        Object asset,
        HashSet<string> dependencies)
    {
        if (asset == null)
            return;

        string assetPath =
            AssetDatabase.GetAssetPath(asset);

        if (!string.IsNullOrEmpty(assetPath))
        {
            dependencies.Add(assetPath);
        }
    }

    private void CopyTerrainLayer(
        TerrainLayer srcLayer,
        string layerDir)
    {
        string layerPath =
            AssetDatabase.GetAssetPath(srcLayer);

        string dstLayerPath =
            GetExportedLayerPath(srcLayer, layerDir);

        string layerName =
            Path.GetFileNameWithoutExtension(dstLayerPath);

        TerrainLayer dstLayer =
            AssetDatabase.LoadAssetAtPath<TerrainLayer>(dstLayerPath);

        if (dstLayer == null)
        {
            if (!string.IsNullOrEmpty(layerPath))
            {
                AssetDatabase.CopyAsset(layerPath, dstLayerPath);
                dstLayer =
                    AssetDatabase.LoadAssetAtPath<TerrainLayer>(dstLayerPath);
            }
            else
            {
                dstLayer = new TerrainLayer();
                AssetDatabase.CreateAsset(dstLayer, dstLayerPath);
                CopyTerrainLayerSettings(srcLayer, dstLayer);
            }
        }

        if (dstLayer == null)
            return;

        CopyTerrainLayerTextures(srcLayer, dstLayer, layerDir, layerName);
        CopyTerrainLayerSettings(srcLayer, dstLayer);
        EditorUtility.SetDirty(dstLayer);
    }

    private void CopyTerrainLayerTextures(
        TerrainLayer srcLayer,
        TerrainLayer dstLayer,
        string layerDir,
        string layerName)
    {
        dstLayer.diffuseTexture =
            CopyLayerTexture(srcLayer.diffuseTexture, layerDir, layerName, "D");

        dstLayer.normalMapTexture =
            CopyLayerTexture(srcLayer.normalMapTexture, layerDir, layerName, "N");

        dstLayer.maskMapTexture =
            CopyLayerTexture(srcLayer.maskMapTexture, layerDir, layerName, "M");
    }

    private void CopyTerrainLayerSettings(
        TerrainLayer srcLayer,
        TerrainLayer dstLayer)
    {
        dstLayer.tileSize = srcLayer.tileSize;
        dstLayer.tileOffset = srcLayer.tileOffset;
        dstLayer.specular = srcLayer.specular;
        dstLayer.metallic = srcLayer.metallic;
        dstLayer.smoothness = srcLayer.smoothness;
        dstLayer.normalScale = srcLayer.normalScale;
        dstLayer.diffuseRemapMin = srcLayer.diffuseRemapMin;
        dstLayer.diffuseRemapMax = srcLayer.diffuseRemapMax;
        dstLayer.maskMapRemapMin = srcLayer.maskMapRemapMin;
        dstLayer.maskMapRemapMax = srcLayer.maskMapRemapMax;
    }

    private Texture2D CopyLayerTexture(
        Texture2D srcTexture,
        string layerDir,
        string layerName,
        string suffix)
    {
        if (srcTexture == null)
            return null;

        string srcPath =
            AssetDatabase.GetAssetPath(srcTexture);

        if (string.IsNullOrEmpty(srcPath))
            return srcTexture;

        string ext =
            Path.GetExtension(srcPath);

        string dstPath =
            $"{layerDir}/{layerName}_{suffix}{ext}";

        if (!File.Exists(dstPath))
        {
            AssetDatabase.CopyAsset(srcPath, dstPath);
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(dstPath);
    }

    private string GetExportedLayerPath(
        TerrainLayer terrainLayer,
        string layerDir)
    {
        string layerName =
            GetLayerExportName(terrainLayer);

        return $"{layerDir}/{layerName}.terrainlayer";
    }

    private void CopyModel(
        string src,
        string treeName,
        string targetDir,
        Dictionary<string, Mesh> exportedMeshMap)
    {
        string ext =
            Path.GetExtension(src);

        if (string.IsNullOrEmpty(ext))
        {
            ext = ".fbx";
        }

        string modelName =
            SanitizeFileName(Path.GetFileNameWithoutExtension(src));

        if (string.IsNullOrEmpty(modelName))
        {
            modelName = "Model";
        }

        string dst =
            $"{targetDir}/{treeName}_{modelName}{ext}";

        if (!File.Exists(dst))
        {
            AssetDatabase.CopyAsset(src, dst);
        }

        RegisterCopiedMeshMappings(src, dst, exportedMeshMap);
    }

    private bool IsModelFileExtension(string ext)
    {
        return ext == ".fbx" ||
               ext == ".obj" ||
               ext == ".dae" ||
               ext == ".blend";
    }

    private void CopyMeshAssetsFallback(
        GameObject sourceObject,
        string treeName,
        string modelDir,
        Dictionary<string, Mesh> exportedMeshMap)
    {
        HashSet<string> meshPaths =
            new HashSet<string>();

        MeshFilter[] meshFilters =
            sourceObject.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            AddAssetPath(meshFilter.sharedMesh, meshPaths);
        }

        SkinnedMeshRenderer[] skinnedMeshes =
            sourceObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (SkinnedMeshRenderer skinnedMesh in skinnedMeshes)
        {
            AddAssetPath(skinnedMesh.sharedMesh, meshPaths);
        }

        foreach (string meshPath in meshPaths)
        {
            string ext =
                Path.GetExtension(meshPath);

            if (string.IsNullOrEmpty(ext))
            {
                ext = ".asset";
            }

            string meshName =
                SanitizeFileName(Path.GetFileNameWithoutExtension(meshPath));

            if (string.IsNullOrEmpty(meshName))
            {
                meshName = "Mesh";
            }

            string dstPath =
                $"{modelDir}/{treeName}_{meshName}{ext}";

            if (!File.Exists(dstPath))
            {
                AssetDatabase.CopyAsset(meshPath, dstPath);
            }

            RegisterCopiedMeshMappings(meshPath, dstPath, exportedMeshMap);
        }
    }

    private void RegisterCopiedMeshMappings(
        string srcPath,
        string dstPath,
        Dictionary<string, Mesh> exportedMeshMap)
    {
        if (exportedMeshMap == null ||
            string.IsNullOrEmpty(srcPath) ||
            string.IsNullOrEmpty(dstPath))
        {
            return;
        }

        Object[] srcAssets =
            AssetDatabase.LoadAllAssetsAtPath(srcPath);

        Object[] dstAssets =
            AssetDatabase.LoadAllAssetsAtPath(dstPath);

        List<Mesh> srcMeshes =
            new List<Mesh>();

        List<Mesh> dstMeshes =
            new List<Mesh>();

        foreach (Object asset in srcAssets)
        {
            if (asset is Mesh srcMesh)
            {
                srcMeshes.Add(srcMesh);
            }
        }

        foreach (Object asset in dstAssets)
        {
            if (asset is Mesh dstMesh)
            {
                dstMeshes.Add(dstMesh);
            }
        }

        int mapCount =
            System.Math.Min(srcMeshes.Count, dstMeshes.Count);

        HashSet<int> mappedSrcIndexes =
            new HashSet<int>();

        HashSet<int> mappedDstIndexes =
            new HashSet<int>();

        Dictionary<long, int> dstByLocalId =
            new Dictionary<long, int>();

        for (int i = 0; i < dstMeshes.Count; i++)
        {
            if (TryGetLocalFileId(dstMeshes[i], out long localId) &&
                !dstByLocalId.ContainsKey(localId))
            {
                dstByLocalId[localId] = i;
            }
        }

        for (int i = 0; i < srcMeshes.Count; i++)
        {
            if (!TryGetLocalFileId(srcMeshes[i], out long localId))
                continue;

            if (!dstByLocalId.TryGetValue(localId, out int dstIndex))
                continue;

            string mapKey =
                GetMeshMapKey(srcMeshes[i]);

            if (string.IsNullOrEmpty(mapKey))
                continue;

            exportedMeshMap[mapKey] = dstMeshes[dstIndex];
            mappedSrcIndexes.Add(i);
            mappedDstIndexes.Add(dstIndex);
        }

        for (int i = 0; i < srcMeshes.Count; i++)
        {
            if (mappedSrcIndexes.Contains(i))
                continue;

            Mesh srcMesh = srcMeshes[i];
            string mapKey = GetMeshMapKey(srcMesh);

            if (string.IsNullOrEmpty(mapKey))
                continue;

            int dstIndex = -1;

            for (int j = 0; j < dstMeshes.Count; j++)
            {
                if (mappedDstIndexes.Contains(j))
                    continue;

                if (dstMeshes[j] != null && dstMeshes[j].name == srcMesh.name)
                {
                    dstIndex = j;
                    break;
                }
            }

            if (dstIndex < 0)
                continue;

            exportedMeshMap[mapKey] = dstMeshes[dstIndex];
            mappedSrcIndexes.Add(i);
            mappedDstIndexes.Add(dstIndex);
        }

        for (int i = 0; i < mapCount; i++)
        {
            if (mappedSrcIndexes.Contains(i) || mappedDstIndexes.Contains(i))
                continue;

            string mapKey =
                GetMeshMapKey(srcMeshes[i]);

            if (string.IsNullOrEmpty(mapKey))
                continue;

            exportedMeshMap[mapKey] = dstMeshes[i];
            mappedSrcIndexes.Add(i);
            mappedDstIndexes.Add(i);
        }
    }

    private bool TryGetLocalFileId(Object asset, out long localFileId)
    {
        localFileId = 0;

        if (asset == null)
            return false;

        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out localFileId);
    }

    private string GetMeshMapKey(Mesh mesh)
    {
        if (mesh == null)
            return string.Empty;

        string meshPath =
            AssetDatabase.GetAssetPath(mesh);

        if (string.IsNullOrEmpty(meshPath))
            return string.Empty;

        GlobalObjectId globalId =
            GlobalObjectId.GetGlobalObjectIdSlow(mesh);

        return globalId.ToString();
    }

    private void RemapExportedMeshes(
        GameObject sourceRoot,
        GameObject targetRoot,
        Dictionary<string, Mesh> exportedMeshMap)
    {
        if (sourceRoot == null ||
            targetRoot == null ||
            exportedMeshMap == null ||
            exportedMeshMap.Count == 0)
        {
            return;
        }

        MeshFilter[] sourceMeshFilters =
            sourceRoot.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter sourceFilter in sourceMeshFilters)
        {
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                continue;

            string meshKey =
                GetMeshMapKey(sourceFilter.sharedMesh);

            if (string.IsNullOrEmpty(meshKey) ||
                !exportedMeshMap.TryGetValue(meshKey, out Mesh mappedMesh) ||
                mappedMesh == null)
            {
                continue;
            }

            string path =
                GetRelativeTransformPath(sourceRoot.transform, sourceFilter.transform);

            Transform targetTransform =
                FindRelativeTransform(targetRoot.transform, path);

            if (targetTransform == null)
                continue;

            MeshFilter targetFilter =
                targetTransform.GetComponent<MeshFilter>();

            if (targetFilter == null)
                continue;

            targetFilter.sharedMesh = mappedMesh;
            EditorUtility.SetDirty(targetFilter);
        }

        SkinnedMeshRenderer[] sourceSkinnedMeshes =
            sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (SkinnedMeshRenderer sourceSkinned in sourceSkinnedMeshes)
        {
            if (sourceSkinned == null || sourceSkinned.sharedMesh == null)
                continue;

            string meshKey =
                GetMeshMapKey(sourceSkinned.sharedMesh);

            if (string.IsNullOrEmpty(meshKey) ||
                !exportedMeshMap.TryGetValue(meshKey, out Mesh mappedMesh) ||
                mappedMesh == null)
            {
                continue;
            }

            string path =
                GetRelativeTransformPath(sourceRoot.transform, sourceSkinned.transform);

            Transform targetTransform =
                FindRelativeTransform(targetRoot.transform, path);

            if (targetTransform == null)
                continue;

            SkinnedMeshRenderer targetSkinned =
                targetTransform.GetComponent<SkinnedMeshRenderer>();

            if (targetSkinned == null)
                continue;

            targetSkinned.sharedMesh = mappedMesh;
            EditorUtility.SetDirty(targetSkinned);
        }
    }

    private Material CopyMaterial(
        Material srcMat,
        string treeName,
        string matDir,
        string texDir,
        string materialTag)
    {
        string suffix =
            GetMaterialType(srcMat.name);

        string safeTag =
            SanitizeFileName(materialTag);

        string tagPart =
            string.IsNullOrEmpty(safeTag) ? string.Empty : $"_{safeTag}";

        string matPath =
            $"{matDir}/{treeName}_{suffix}{tagPart}.mat";

        if (!File.Exists(matPath))
        {
            AssetDatabase.CopyAsset(
                AssetDatabase.GetAssetPath(srcMat),
                matPath);
        }

        Material dstMat =
            AssetDatabase.LoadAssetAtPath<Material>(matPath);

        if (dstMat == null)
        {
            return srcMat;
        }

        foreach (TextureExportRule rule in TextureExportRules)
        {
            ProcessTexture(
                dstMat,
                rule.Property,
                rule.Suffix,
                treeName,
                suffix,
                safeTag,
                texDir);
        }

        EditorUtility.SetDirty(dstMat);

        return dstMat;
    }

    private void ProcessTexture(
        Material mat,
        string property,
        string texSuffix,
        string treeName,
        string matSuffix,
        string materialTag,
        string texDir)
    {
        if (!mat.HasProperty(property))
            return;

        Texture tex =
            mat.GetTexture(property);

        if (tex == null)
            return;

        string srcPath =
            AssetDatabase.GetAssetPath(tex);

        string ext =
            Path.GetExtension(srcPath);

        string tagPart =
            string.IsNullOrEmpty(materialTag) ? string.Empty : $"_{materialTag}";

        string dstPath =
            $"{texDir}/{treeName}_{matSuffix}{tagPart}_{texSuffix}{ext}";

        if (!File.Exists(dstPath))
        {
            AssetDatabase.CopyAsset(srcPath, dstPath);
        }

        Texture newTex =
            AssetDatabase.LoadAssetAtPath<Texture>(dstPath);

        mat.SetTexture(property, newTex);
    }

    private string GetMaterialType(string name)
    {
        string lower = name.ToLower();

        if (lower.Contains("bark"))
            return "Bark";

        if (lower.Contains("leaf"))
            return "Leaf";

        if (lower.Contains("trunk"))
            return "Trunk";

        if (lower.Contains("branch"))
            return "Branch";

        return "Mat";
    }

    private string GetOutputRootPath()
    {
        if (outputRootFolder == null)
        {
            const string defaultPath = "Assets/Fix";
            CreateFolder(defaultPath);
            return defaultPath;
        }

        string outputRootPath =
            AssetDatabase.GetAssetPath(outputRootFolder);

        if (string.IsNullOrEmpty(outputRootPath) ||
            !AssetDatabase.IsValidFolder(outputRootPath))
        {
            Debug.LogError("输出总目录必须是 Project 视图中的有效文件夹");
            return null;
        }

        return outputRootPath;
    }

    private string GetLayerExportName(TerrainLayer terrainLayer)
    {
        string layerPath =
            AssetDatabase.GetAssetPath(terrainLayer);

        if (!string.IsNullOrEmpty(layerPath) &&
            layerNameOverrides.TryGetValue(layerPath, out string overrideName))
        {
            string sanitizedName = SanitizeFileName(overrideName);

            if (!string.IsNullOrEmpty(sanitizedName))
            {
                return sanitizedName;
            }
        }

        return terrainLayer.name;
    }

    private string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value.Trim();

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar.ToString(), string.Empty);
        }

        return sanitized.Trim();
    }

    private void CreateFolder(string path)
    {
        string[] dirs = path.Split('/');

        string current = dirs[0];

        for (int i = 1; i < dirs.Length; i++)
        {
            string next = current + "/" + dirs[i];

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(
                    current,
                    dirs[i]);
            }

            current = next;
        }
    }
}

}

