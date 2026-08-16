using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EAStudio.Core.Editor
{
    public static class LightmapAnalyzer
    {
        [MenuItem("Tools/EAStudio/光照/光照贴图使用分析")]
        public static void Analyze()
        {
            int bakedCount = 0;
            int lightmapZeroCount = 0;
            int probeCount = 0;
            int nonStaticCount = 0;

            Debug.Log("========== Lightmap Analyze Begin ==========");

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

                foreach (MeshRenderer r in renderers)
                {
                    bool contributeGI =
                        GameObjectUtility.AreStaticEditorFlagsSet(
                            r.gameObject,
                            StaticEditorFlags.ContributeGI
                        );

                    if (!contributeGI)
                    {
                        nonStaticCount++;
                        continue;
                    }

                    if (r.receiveGI == ReceiveGI.Lightmaps)
                    {
                        bakedCount++;

                        if (r.lightmapIndex == 0)
                        {
                            lightmapZeroCount++;
                            Debug.Log(
                                $"[LM0] {r.name} | ScaleInLightmap: {r.scaleInLightmap}",
                                r
                            );
                        }
                    }
                    else if (r.receiveGI == ReceiveGI.LightProbes)
                    {
                        probeCount++;
                    }
                }
            }

            Debug.Log(
                $"========== Lightmap Analyze Result ==========\n" +
                $"ContributeGI (Baked): {bakedCount}\n" +
                $"   -> On Lightmap 0: {lightmapZeroCount}\n" +
                $"ContributeGI (Probes): {probeCount}\n" +
                $"Non-Static: {nonStaticCount}\n" +
                $"Total Lightmaps: {LightmapSettings.lightmaps.Length}"
            );
        }
    }
}

