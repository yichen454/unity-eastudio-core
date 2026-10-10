using UnityEngine;

namespace EAStudio.Core
{
    /// <summary>Owns the surface inputs for one Terrain and refreshes its assigned material cache.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Terrain))]
    public sealed class TerrainSurfaceProvider : MonoBehaviour
    {
        [SerializeField] Terrain terrain;
        [SerializeField] Texture2D surface;
        [SerializeField] Texture2D detail;
        [SerializeField] Material[] generatedMaterials = System.Array.Empty<Material>();

        static readonly int SurfaceId = Shader.PropertyToID("_TerrainSurface");
        static readonly int DetailId = Shader.PropertyToID("_TerrainDetail");
        static readonly int HasDetailId = Shader.PropertyToID("_TerrainHasDetail");
        static readonly int HeightmapId = Shader.PropertyToID("_TerrainHeightmap");
        static readonly int PositionId = Shader.PropertyToID("_TerrainPosition");
        static readonly int SizeId = Shader.PropertyToID("_TerrainSize");

        public Terrain Terrain => terrain;
        public Texture2D Surface => surface;
        public Texture2D Detail => detail;

        void Reset() => terrain = GetComponent<Terrain>();
        void OnEnable() => RefreshBindings();

        public void RefreshBindings()
        {
            if (!terrain || terrain.gameObject != gameObject || !terrain.terrainData || !surface ||
                Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > .001f ||
                (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-8f) return;

            TerrainData data = terrain.terrainData;
            Vector3 position = terrain.transform.position;
            Vector3 size = data.size;
            Texture heightmap = data.heightmapTexture;
            if (!heightmap || size.x <= 0 || size.z <= 0) return;

            foreach (Material material in generatedMaterials)
            {
                if (!material) continue;
                if (material.HasProperty(SurfaceId)) material.SetTexture(SurfaceId, surface);
                if (material.HasProperty(DetailId)) material.SetTexture(DetailId, detail);
                if (material.HasProperty(HasDetailId)) material.SetFloat(HasDetailId, detail ? 1f : 0f);
                if (material.HasProperty(HeightmapId)) material.SetTexture(HeightmapId, heightmap);
                if (material.HasProperty(PositionId)) material.SetVector(PositionId, position);
                if (material.HasProperty(SizeId)) material.SetVector(SizeId, size);
            }
        }
    }
}
