using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    [CustomEditor(typeof(TerrainSurfaceProvider))]
    public sealed class TerrainSurfaceProviderEditor : UnityEditor.Editor
    {
        TerrainColorMapBakeAsset recipe;
        string recipeStatus;

        void RefreshRecipeStatus()
        {
            try { recipeStatus = recipe ? TerrainColorMapBaker.GetStatus(recipe) : null; }
            catch (System.Exception error) { recipeStatus = error.Message; }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("terrain"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("surface"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("detail"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generatedMaterials"), true);
            serializedObject.ApplyModifiedProperties();

            var provider = (TerrainSurfaceProvider)target;
            if (!provider.Terrain || !provider.Terrain.terrainData)
                EditorGUILayout.HelpBox("Assign a Terrain with TerrainData.", MessageType.Error);
            else if (provider.Terrain.gameObject != provider.gameObject)
                EditorGUILayout.HelpBox("The Provider must reference the Terrain on the same GameObject.", MessageType.Error);
            else if (Quaternion.Angle(provider.Terrain.transform.rotation, Quaternion.identity) > .001f ||
                (provider.Terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-8f)
                EditorGUILayout.HelpBox("Terrain must have identity rotation and unit world scale.", MessageType.Error);
            if (!provider.Surface)
                EditorGUILayout.HelpBox("Surface is required.", MessageType.Warning);

            EditorGUI.BeginChangeCheck();
            recipe = (TerrainColorMapBakeAsset)EditorGUILayout.ObjectField("Surface Recipe", recipe, typeof(TerrainColorMapBakeAsset), false);
            if (EditorGUI.EndChangeCheck()) RefreshRecipeStatus();
            if (recipe && GUILayout.Button("Validate Surface Recipe")) RefreshRecipeStatus();
            using (new EditorGUI.DisabledScope(!recipe || !provider.Terrain || provider.Terrain.terrainData != recipe.terrainData || recipeStatus != "Up to Date"))
            {
                if (GUILayout.Button("Assign Verified Outputs"))
                {
                    Undo.RecordObject(provider, "Assign Terrain Surface Outputs");
                    serializedObject.FindProperty("surface").objectReferenceValue = recipe.outputTexture;
                    serializedObject.FindProperty("detail").objectReferenceValue = recipe.bakeDetail ? recipe.detailTexture : null;
                    serializedObject.ApplyModifiedProperties();
                }
            }
            if (recipe && provider.Terrain && provider.Terrain.terrainData != recipe.terrainData)
                EditorGUILayout.HelpBox("The recipe uses a different TerrainData.", MessageType.Error);
            else if (recipe && recipeStatus != "Up to Date")
                EditorGUILayout.HelpBox("Bake or rebake the selected recipe before assignment. Status: " + recipeStatus, MessageType.Warning);

            if (GUILayout.Button("Refresh Assigned Material Bindings")) provider.RefreshBindings();
        }
    }
}
