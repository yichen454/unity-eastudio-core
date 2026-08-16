using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    public class TerrainTreeConverterWindow : EditorWindow
    {
        private Terrain selectTerrain;
        private bool clearOriginTree = true;
        private bool setOccludeeStatic = true;
        private Transform rootParent;

        [MenuItem("Tools/EAStudio/地形/地形树转实体 GameObject")]
        public static void OpenWindow()
        {
            GetWindow<TerrainTreeConverterWindow>("地形树转实体 GameObject");
        }

        private void OnGUI()
        {
            selectTerrain = EditorGUILayout.ObjectField("目标地形", selectTerrain, typeof(Terrain), true) as Terrain;
            rootParent = EditorGUILayout.ObjectField("生成父物体(空=场景根)", rootParent, typeof(Transform), true) as Transform;
            clearOriginTree = EditorGUILayout.Toggle("转换后清空地形原有树木", clearOriginTree);
            setOccludeeStatic = EditorGUILayout.Toggle("自动标记Occludee Static(遮挡剔除)", setOccludeeStatic);

            if (GUILayout.Button("开始批量转换"))
            {
                ConvertTreeToObj();
            }
        }

        private void ConvertTreeToObj()
        {
            if (selectTerrain == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择Terrain地形", "OK");
                return;
            }

            TerrainData td = selectTerrain.terrainData;
            TreeInstance[] allTree = td.treeInstances;
            List<TreeInstance> newTreeList = new List<TreeInstance>();

            Transform treeRoot = rootParent;
            if (treeRoot == null)
            {
                GameObject rootObj = new GameObject("Spawn_Tree_Root");
                treeRoot = rootObj.transform;
            }

            int count = 0;
            foreach (var tree in allTree)
            {
                Vector3 localPos = Vector3.Scale(tree.position, td.size);
                Vector3 worldPos = selectTerrain.transform.TransformPoint(localPos);
                TreePrototype proto = td.treePrototypes[tree.prototypeIndex];
                GameObject prefab = proto.prefab;
                if (prefab == null) continue;

                Quaternion worldRot = selectTerrain.transform.rotation
                                    * Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f)
                                    * prefab.transform.rotation;
                Vector3 treeRandomScale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
                Vector3 worldScale = Vector3.Scale(prefab.transform.localScale, treeRandomScale);
                worldScale = Vector3.Scale(worldScale, selectTerrain.transform.lossyScale);
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(
                    prefab,
                    selectTerrain.gameObject.scene
                );

                if (inst == null)
                    continue;

                inst.transform.SetPositionAndRotation(worldPos, worldRot);
                inst.transform.localScale = worldScale;
                inst.transform.SetParent(treeRoot, true);

                if (setOccludeeStatic)
                    inst.isStatic = true;

                count++;
                if (!clearOriginTree) newTreeList.Add(tree);
            }

            if (clearOriginTree)
            {
                td.treeInstances = new TreeInstance[0];
            }
            else
            {
                td.treeInstances = newTreeList.ToArray();
            }

            td.RefreshPrototypes();
            EditorUtility.SetDirty(td);
            EditorUtility.DisplayDialog("完成", $"成功生成{count}棵实体树木", "OK");
        }
    }
}

