using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EAStudio.Core.Editor
{
    public static class LightmapAnalyzer
    {
        public sealed class Entry
        {
            public MeshRenderer Renderer;
            public string Path;
            public bool ContributeGI;
            public ReceiveGI ReceiveGI;
            public float Scale;
            public int Index;
            public Vector4 ScaleOffset;
            public bool Bound;
            public string Issues;
        }

        [MenuItem("Tools/EAStudio/光照/光照贴图分析")]
        public static void Analyze() => LightmapAnalyzerWindow.Open();

        public static List<Entry> Scan(IEnumerable<GameObject> roots, bool includeInactive, LightmapData[] maps)
        {
            var entries = new List<Entry>();
            var visited = new HashSet<int>();
            foreach (var root in roots)
            {
                if (!root || !root.scene.IsValid() || EditorUtility.IsPersistent(root)) continue;
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(includeInactive))
                {
                    if (!visited.Add(renderer.GetInstanceID())) continue;
                    int index = renderer.lightmapIndex;
                    bool assigned = index >= 0 && index < 65534;
                    bool bound = assigned && index < maps.Length && maps[index] != null && maps[index].lightmapColor;
                    var entry = new Entry
                    {
                        Renderer = renderer,
                        Path = GetPath(renderer.transform),
                        ContributeGI = GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.ContributeGI),
                        ReceiveGI = renderer.receiveGI,
                        Scale = renderer.scaleInLightmap,
                        Index = index,
                        ScaleOffset = renderer.lightmapScaleOffset,
                        Bound = bound
                    };
                    var issues = new List<string>();
                    if (assigned && !bound) issues.Add("图集索引无效或缺少颜色贴图");
                    if (entry.ContributeGI && entry.ReceiveGI == ReceiveGI.Lightmaps && entry.Scale > 0f && !bound)
                        issues.Add("已配置接收光照贴图，但没有有效的烘焙绑定（可能尚未烘焙）");
                    if (entry.ContributeGI && entry.ReceiveGI == ReceiveGI.Lightmaps && entry.Scale == 0f)
                        issues.Add("光照贴图缩放为零（不分配图集空间）");
                    if (bound && (entry.ScaleOffset.x <= 0f || entry.ScaleOffset.y <= 0f))
                        issues.Add("光照贴图 UV 缩放小于或等于零");
                    entry.Issues = string.Join("；", issues);
                    entries.Add(entry);
                }
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return entries;
        }

        private static string GetPath(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            Scene scene = transform.gameObject.scene;
            return scene.name + ":/" + path;
        }
    }
}
