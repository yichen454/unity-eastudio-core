using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    public class TerrainMergeTool : EditorWindow
    {
        [MenuItem("Tools/EAStudio/地形/合并选中地形")]
        public static void MergeSelectedTerrains()
        {
            Terrain[] terrains = Selection.gameObjects
                .Select(x => x.GetComponent<Terrain>())
                .Where(x => x != null)
                .ToArray();

            if (terrains.Length < 2)
            {
                Debug.LogError("请至少选择两个 Terrain");
                return;
            }

            Merge(terrains);
        }

        private static void Merge(Terrain[] terrains)
        {
            float minX = terrains.Min(t => t.transform.position.x);
            float minZ = terrains.Min(t => t.transform.position.z);
            float maxX = terrains.Max(t => t.transform.position.x + t.terrainData.size.x);
            float maxZ = terrains.Max(t => t.transform.position.z + t.terrainData.size.z);
            float maxHeight = terrains.Max(t => t.terrainData.size.y);

            Vector3 totalSize = new Vector3(maxX - minX, maxHeight, maxZ - minZ);

            Terrain baseTerrain = terrains[0];
            TerrainData baseData = baseTerrain.terrainData;

            int heightmapRes = baseData.heightmapResolution;
            int alphamapRes = baseData.alphamapResolution;

            float sampleTileSizeX = baseData.size.x;
            float sampleTileSizeZ = baseData.size.z;

            int tilesX = Mathf.RoundToInt(totalSize.x / sampleTileSizeX);
            int tilesZ = Mathf.RoundToInt(totalSize.z / sampleTileSizeZ);

            int mergedHeightRes = (heightmapRes - 1) * tilesX + 1;
            int mergedAlphaResX = alphamapRes * tilesX;
            int mergedAlphaResZ = alphamapRes * tilesZ;

            TerrainData mergedData = new TerrainData();
            mergedData.heightmapResolution = mergedHeightRes;
            mergedData.size = totalSize;

            mergedData.terrainLayers = baseData.terrainLayers;
            mergedData.alphamapResolution = mergedAlphaResX;

            float[,] mergedHeights = new float[mergedHeightRes, mergedHeightRes];
            float[,,] mergedAlphamaps = new float[
                mergedAlphaResZ,
                mergedAlphaResX,
                mergedData.terrainLayers.Length
            ];

            foreach (var t in terrains)
            {
                TerrainData td = t.terrainData;
                Vector3 pos = t.transform.position;

                int tileOffsetX = Mathf.RoundToInt((pos.x - minX) / sampleTileSizeX);
                int tileOffsetZ = Mathf.RoundToInt((pos.z - minZ) / sampleTileSizeZ);

                float[,] h = td.GetHeights(0, 0, heightmapRes, heightmapRes);
                int baseStartX = tileOffsetX * (heightmapRes - 1);
                int baseStartZ = tileOffsetZ * (heightmapRes - 1);

                for (int z = 0; z < heightmapRes; z++)
                {
                    for (int x = 0; x < heightmapRes; x++)
                    {
                        mergedHeights[baseStartZ + z, baseStartX + x] = h[z, x];
                    }
                }

                float[,,] a = td.GetAlphamaps(0, 0, alphamapRes, alphamapRes);
                int aStartX = tileOffsetX * alphamapRes;
                int aStartZ = tileOffsetZ * alphamapRes;

                for (int z = 0; z < alphamapRes; z++)
                {
                    for (int x = 0; x < alphamapRes; x++)
                    {
                        for (int l = 0; l < td.terrainLayers.Length; l++)
                        {
                            mergedAlphamaps[aStartZ + z, aStartX + x, l] = a[z, x, l];
                        }
                    }
                }
            }

            mergedData.SetHeights(0, 0, mergedHeights);
            mergedData.SetAlphamaps(0, 0, mergedAlphamaps);

            string path = "Assets/MergedTerrain.asset";
            AssetDatabase.CreateAsset(
                mergedData,
                AssetDatabase.GenerateUniqueAssetPath(path)
            );

            GameObject go = Terrain.CreateTerrainGameObject(mergedData);
            go.name = "MergedTerrain";
            go.transform.position = new Vector3(minX, 0, minZ);

            Selection.activeGameObject = go;

            Debug.Log(
                $"Merge Complete\n" +
                $"Height = {mergedHeightRes}\n" +
                $"Alpha = {mergedAlphaResX}\n" +
                $"Layers = {mergedData.terrainLayers.Length}\n" +
                $"Size = {mergedData.size}"
            );
        }
    }
}

