using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EAStudio.Core.SceneWarmup
{
    /// <summary>
    /// Serializable scene path wrapper that stores only the scene path without holding a <c>SceneAsset</c> reference.
    /// </summary>
    [Serializable]
    public class SceneReference
    {
        [SerializeField] private string scenePath = string.Empty;

        /// <summary>Full asset path, e.g. "Assets/Scenes/MyScene.unity".</summary>
        public string ScenePath
        {
            get => scenePath;
            set => scenePath = value ?? string.Empty;
        }

        /// <summary>Scene name without path or extension.</summary>
        public string SceneName => string.IsNullOrEmpty(scenePath) ? string.Empty : Path.GetFileNameWithoutExtension(scenePath);

        /// <summary>True when a valid scene path has been assigned.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(scenePath);

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
    }
}