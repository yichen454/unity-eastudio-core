using UnityEngine;

namespace EAStudio.Core.Editor
{
    /// <summary>Editor-only recipe and the revision of its last verified Surface outputs.</summary>
    public sealed class TerrainColorMapBakeAsset : ScriptableObject
    {
        public TerrainData terrainData;
        [Tooltip("Leave empty for stock Terrain/Lit defaults.")]
        public Material material;
        public int width = 1024;
        public int height = 1024;
        public bool bakeDetail;

        [HideInInspector] public Texture2D outputTexture;
        [HideInInspector] public Texture2D detailTexture;
        [HideInInspector] public string detailGuid;
        [HideInInspector] public string outputGuid;
        [HideInInspector] public int outputWidth;
        [HideInInspector] public int outputHeight;
        [HideInInspector] public Vector2 sourceSizeXZ;
        [HideInInspector] public int coordinateVersion;
        [HideInInspector] public string encoding;
        [HideInInspector] public string bakerVersion;
        [HideInInspector] public string fingerprint;
        [HideInInspector] public string completedUtc;
    }
}
