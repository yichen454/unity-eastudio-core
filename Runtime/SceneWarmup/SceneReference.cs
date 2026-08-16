using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EA.Timeline
{
    /// <summary>
    /// Serializable scene reference that supports drag-and-drop assignment in the Inspector.
    /// Stores a <see cref="SceneAsset"/> in the Editor and serializes the scene path for runtime.
    /// </summary>
    [Serializable]
    public class SceneReference : ISerializationCallbackReceiver
    {
#if UNITY_EDITOR
        [SerializeField] private SceneAsset sceneAsset;
#endif
        [SerializeField] private string scenePath = string.Empty;

        /// <summary>Full asset path, e.g. "Assets/Scenes/MyScene.unity".</summary>
        public string ScenePath => scenePath;

        /// <summary>Scene name without path or extension.</summary>
        public string SceneName => Path.GetFileNameWithoutExtension(scenePath);

        /// <summary>True when a valid scene has been assigned.</summary>
        public bool IsValid => !string.IsNullOrEmpty(scenePath);

        /// <summary>Asynchronously loads the scene in the given mode.</summary>
        public AsyncOperation LoadAsync(LoadSceneMode mode = LoadSceneMode.Additive)
        {
            if (!IsValid)
            {
                Debug.LogWarning("[SceneReference] No scene assigned.");
                return null;
            }
            return SceneManager.LoadSceneAsync(scenePath, mode);
        }

        // Keep scenePath in sync with the dragged SceneAsset.
        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            scenePath = sceneAsset != null ? AssetDatabase.GetAssetPath(sceneAsset) : string.Empty;
#endif
        }

        public void OnAfterDeserialize() { }
    }
}
