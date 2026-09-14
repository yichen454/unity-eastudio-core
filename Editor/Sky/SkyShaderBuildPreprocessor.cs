using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using EAStudio.Core.RenderFeature.Sky;

namespace EAStudio.Core.Editor.Sky
{
    /// <summary>
    /// Guarantees that all Sky and Cloud shaders are referenced and preserved during player builds.
    /// </summary>
    public class SkyShaderBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null)
                    continue;

                bool modified = false;
                foreach (var feature in rendererData.rendererFeatures)
                {
                    if (feature is SkyRenderFeature skyFeature)
                    {
                        skyFeature.EnsureShaders();
                        EditorUtility.SetDirty(rendererData);
                        modified = true;
                    }
                }

                if (modified)
                {
                    AssetDatabase.SaveAssets();
                }
            }
        }
    }
}
