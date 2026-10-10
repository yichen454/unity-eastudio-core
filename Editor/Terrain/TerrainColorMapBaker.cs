using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EAStudio.Core.Editor
{
    /// <summary>One Editor GPU operation at a time; recipes publish only verified results.</summary>
    public static class TerrainColorMapBaker
    {
        public const string Version = "3.0.0-surface-detail-urp17.3";
        const string ShaderPath = "Packages/com.eastudio.core/Editor/Terrain/TerrainColorMapBaker.compute";
        static readonly string[] Platforms = { "Standalone", "Android", "iPhone", "WebGL", "Windows Store Apps", "PS4", "PS5", "XboxOne", "Nintendo Switch" };
        static readonly int[] Dimensions = { 256, 512, 1024, 2048, 4096 };
        static Operation active;
        public static bool IsBusy => active != null;

        public static string Extract(Terrain terrain, TerrainColorMapBakeAsset recipe)
        {
            if (!terrain || !terrain.terrainData) return "Select a Terrain with TerrainData.";
            if (Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > .001f ||
                (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-8f)
                return "Source Terrain must have identity rotation and unit world scale.";
            var block = new MaterialPropertyBlock();
            terrain.GetSplatMaterialPropertyBlock(block);
            if (!block.isEmpty) return "Terrain material property overrides are unsupported. Clear its splat property block.";
            recipe.terrainData = terrain.terrainData;
            recipe.material = terrain.materialTemplate;
            return null;
        }

        public static string OutputPath(TerrainColorMapBakeAsset recipe)
        {
            string path = AssetDatabase.GetAssetPath(recipe);
            if (string.IsNullOrEmpty(path)) return null;
            string name = Path.GetFileNameWithoutExtension(path);
            bool legacy = name.EndsWith("_ColorMapBake", StringComparison.Ordinal);
            if (legacy) name = name.Substring(0, name.Length-13);
            else if (name.EndsWith("_SurfaceBake", StringComparison.Ordinal)) name = name.Substring(0, name.Length-12);
            return Path.GetDirectoryName(path).Replace('\\','/') + "/" + name + (legacy ? "_ColorMap.png" : "_Surface.png");
        }

        public static string DetailPath(TerrainColorMapBakeAsset recipe)
        {
            string surface = OutputPath(recipe);
            return string.IsNullOrEmpty(surface) ? null : surface.Substring(0, surface.Length - 4) + "_Detail.png";
        }

        public static List<string> Validate(TerrainColorMapBakeAsset recipe, bool requireOutput = true)
        {
            var errors = new List<string>();
            if (!recipe) { errors.Add("Choose a bake recipe."); return errors; }
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) errors.Add("Use Linear project color space.");
            if (!SystemInfo.supportsComputeShaders || (!SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore) || !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.ReadPixels)))
                errors.Add("Device must support compute shaders and RGBAFloat random-write targets.");
            
            if (Array.IndexOf(Dimensions, recipe.width) < 0 || Array.IndexOf(Dimensions, recipe.height) < 0)
                errors.Add("Width and height must independently be 256, 512, 1024, 2048 or 4096.");
            if (recipe.width > SystemInfo.maxTextureSize || recipe.height > SystemInfo.maxTextureSize) errors.Add("Resolution exceeds this device's texture limit.");
            var data = recipe.terrainData;
            if (!data || !EditorUtility.IsPersistent(data)) errors.Add("Choose a persistent TerrainData asset.");
            else
            {
                if (!Finite(data.size.x) || !Finite(data.size.z) || data.size.x <= 0 || data.size.z <= 0) errors.Add("Terrain XZ size must be finite and positive.");
                var layers = data.terrainLayers;
                if (layers.Length == 0 || data.alphamapTextureCount != (layers.Length+3)/4) errors.Add("Terrain requires layers and matching alphamap groups.");
                for (int i=0;i<layers.Length;i++)
                {
                    var layer = layers[i];
                    if (!layer || !EditorUtility.IsPersistent(layer)) { errors.Add($"Layer {i}: choose a persistent TerrainLayer."); continue; }
                    if (!layer.diffuseTexture || !EditorUtility.IsPersistent(layer.diffuseTexture)) errors.Add($"Layer {i}: diffuse texture must be a persistent asset.");
                    if (layer.maskMapTexture && !EditorUtility.IsPersistent(layer.maskMapTexture)) errors.Add($"Layer {i}: mask texture must be a persistent asset.");
                    if (recipe.bakeDetail && layer.normalMapTexture && !EditorUtility.IsPersistent(layer.normalMapTexture)) errors.Add($"Layer {i}: normal texture must be a persistent asset.");
                    if (!Finite(layer.tileSize.x) || !Finite(layer.tileSize.y) || layer.tileSize.x <= 0 || layer.tileSize.y <= 0 ||
                        !Finite(layer.tileOffset.x) || !Finite(layer.tileOffset.y)) errors.Add($"Layer {i}: use positive tile sizes and finite offsets.");
                    if (!Finite(layer.diffuseRemapMin) || !Finite(layer.diffuseRemapMax) || !Finite(layer.maskMapRemapMin) || !Finite(layer.maskMapRemapMax)) errors.Add($"Layer {i}: remap parameters must be finite.");
                }
            }
            if (recipe.material)
            {
                if (!EditorUtility.IsPersistent(recipe.material) || recipe.material.shader != Shader.Find("Universal Render Pipeline/Terrain/Lit"))
                    errors.Add("Unsupported terrain shader. Use a persistent stock Universal Render Pipeline/Terrain/Lit material or stock defaults.");
                else if (!Finite(recipe.material.GetFloat("_HeightTransition")) || !Finite(recipe.material.GetFloat("_EnableHeightBlend")))
                    errors.Add("Material blend parameters must be finite.");
                else if ((recipe.material.GetFloat("_EnableHeightBlend") > 0) != recipe.material.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"))
                    errors.Add("Height-blend toggle and shader keyword disagree. Reopen the material Inspector to synchronize them.");
            }
            if (requireOutput)
            {
                string path = AssetDatabase.GetAssetPath(recipe);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Save the recipe as an .asset under writable Assets/ before baking.");
                else
                {
                    string output = OutputPath(recipe);
                    if (!string.IsNullOrEmpty(recipe.outputGuid) && AssetDatabase.GUIDToAssetPath(recipe.outputGuid) != output &&
                        !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(recipe.outputGuid)))
                        errors.Add("Recipe or output was moved. Restore their original sibling paths before rebaking.");
                    if (File.Exists(output) || File.Exists(output+".meta"))
                    {
                        if (string.IsNullOrEmpty(recipe.outputGuid) || AssetDatabase.AssetPathToGUID(output) != recipe.outputGuid)
                            errors.Add("Output path belongs to unrelated content. Choose a different recipe location.");
                    }
                    if (recipe.bakeDetail)
                    {
                        string detail = DetailPath(recipe);
                        if (!string.IsNullOrEmpty(recipe.detailGuid) && AssetDatabase.GUIDToAssetPath(recipe.detailGuid) != detail &&
                            !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(recipe.detailGuid)))
                            errors.Add("Recipe or Detail was moved. Restore their original sibling paths before rebaking.");
                        if ((File.Exists(detail) || File.Exists(detail + ".meta")) &&
                            (string.IsNullOrEmpty(recipe.detailGuid) || AssetDatabase.AssetPathToGUID(detail) != recipe.detailGuid))
                            errors.Add("Detail path belongs to unrelated content. Choose a different recipe location.");
                        var detailImporter = AssetImporter.GetAtPath(detail) as TextureImporter;
                        if (detailImporter) errors.AddRange(PlatformErrors(detailImporter, recipe.width, recipe.height));
                        if (File.Exists(detail) && (File.GetAttributes(detail) & FileAttributes.ReadOnly) != 0)
                            errors.Add("Detail output is read-only.");
                    }
                    var importer = AssetImporter.GetAtPath(output) as TextureImporter;
                    if (importer) errors.AddRange(PlatformErrors(importer,recipe.width,recipe.height));
                    if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 ||
                        (File.Exists(output) && (File.GetAttributes(output) & FileAttributes.ReadOnly) != 0)) errors.Add("Recipe or output is read-only.");
                }
            }
            return errors;
        }

        public static string GetStatus(TerrainColorMapBakeAsset recipe)
        {
            if (Validate(recipe, false).Count > 0) return "Invalid Source";
            if (string.IsNullOrEmpty(recipe.fingerprint)) return "Not Baked";
            string outputPath = AssetDatabase.GUIDToAssetPath(recipe.outputGuid);
            if (!recipe.outputTexture || string.IsNullOrEmpty(outputPath) || !File.Exists(outputPath)) return "Missing Output";
            if (recipe.bakeDetail && (!recipe.detailTexture || AssetDatabase.GUIDToAssetPath(recipe.detailGuid) != DetailPath(recipe) || !File.Exists(DetailPath(recipe)))) return "Missing Output";
            var importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (!ImportMatches(importer, recipe.outputWidth, recipe.outputHeight, recipe.outputTexture) || PlatformErrors(importer,recipe.outputWidth,recipe.outputHeight).Count > 0) return "Stale";
            if (recipe.bakeDetail)
            {
                var detailImporter=AssetImporter.GetAtPath(DetailPath(recipe)) as TextureImporter;
                if (!DetailImportMatches(detailImporter,recipe.outputWidth,recipe.outputHeight,recipe.detailTexture) ||
                    PlatformErrors(detailImporter,recipe.outputWidth,recipe.outputHeight).Count>0) return "Stale";
            }
            return Fingerprint(recipe) == recipe.fingerprint ? "Up to Date" : "Stale";
        }

        public static string Fingerprint(TerrainColorMapBakeAsset recipe)
        {
            var data = recipe.terrainData;
            data.SyncTexture(TerrainData.AlphamapTextureName);
            using (var hash = SHA256.Create())
            using (var bytes = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                writer.Write(Version); writer.Write((int)QualitySettings.activeColorSpace);
                writer.Write(recipe.width); writer.Write(recipe.height); writer.Write(recipe.bakeDetail);
                writer.Write(data.size.x); writer.Write(data.size.z);
                writer.Write(data.alphamapWidth); writer.Write(data.alphamapHeight);
                var layers = data.terrainLayers; writer.Write(layers.Length);
                foreach (var layer in layers)
                {
                    writer.Write(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(layer)));
                    WriteVector(writer, layer.tileSize); WriteVector(writer, layer.tileOffset);
                    WriteVector(writer, layer.diffuseRemapMax-layer.diffuseRemapMin);
                    writer.Write(layer.smoothness);
                    WriteVector(writer, layer.maskMapRemapMin); WriteVector(writer, layer.maskMapRemapMax);
                    WriteTexture(writer, layer.diffuseTexture); WriteTexture(writer, layer.maskMapTexture);
                    if (recipe.bakeDetail) { writer.Write(layer.normalScale); WriteTexture(writer, layer.normalMapTexture); }
                }
                writer.Write(recipe.material ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe.material)) : "stock-default");
                writer.Write(recipe.material ? recipe.material.GetFloat("_EnableHeightBlend") : 0f);
                writer.Write(recipe.material ? recipe.material.GetFloat("_HeightTransition") : 0f);
                writer.Write(recipe.material && recipe.material.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"));
                // Read synchronized resident paint data; do not hash TerrainData height content.
                for(int row=0;row<data.alphamapHeight;row+=32)
                {
                    var weights=data.GetAlphamaps(0,row,data.alphamapWidth,Mathf.Min(32,data.alphamapHeight-row));
                    var raw=new byte[weights.Length*sizeof(float)];
                    Buffer.BlockCopy(weights,0,raw,0,raw.Length); writer.Write(raw);
                }
                if (recipe.bakeDetail)
                {
                    var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                    for (int row=0;row<data.heightmapResolution;row++)
                        for (int column=0;column<data.heightmapResolution;column++) writer.Write(heights[row,column]);
                }
                writer.Flush(); bytes.FlushFinalBlock();
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }

        static void WriteVector(BinaryWriter writer, Vector2 value)
        { writer.Write(value.x); writer.Write(value.y); }
        static void WriteVector(BinaryWriter writer, Vector4 value)
        { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); writer.Write(value.w); }
        static void WriteTexture(BinaryWriter writer, Texture texture)
        {
            if (!texture) { writer.Write(""); return; }
            string path = AssetDatabase.GetAssetPath(texture);
            writer.Write(AssetDatabase.AssetPathToGUID(path));
            writer.Write(AssetDatabase.GetAssetDependencyHash(path).ToString());
            var importer=AssetImporter.GetAtPath(path);
            writer.Write(importer ? EditorJsonUtility.ToJson(importer) : "");
            writer.Write(texture.imageContentsHash.ToString());
            writer.Write((int)texture.filterMode); writer.Write((int)texture.wrapModeU); writer.Write((int)texture.wrapModeV);
            writer.Write(texture.anisoLevel); writer.Write(texture.mipMapBias);
            if(texture is Texture2D texture2D) writer.Write(texture2D.activeMipmapLimit);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector4 v) => Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w);
        public static Operation Preview(TerrainColorMapBakeAsset recipe) => Start(recipe, true);
        public static Operation Bake(TerrainColorMapBakeAsset recipe) => Start(recipe, false);
        static Operation Start(TerrainColorMapBakeAsset recipe, bool preview)
        {
            if (active != null) throw new InvalidOperationException("Another Terrain Surface operation is in progress.");
            active = new Operation(recipe, preview);
            active.Start(); return active;
        }

        public sealed class Operation : IDisposable
        {
            public bool IsDone { get; private set; }
            public string Error { get; private set; }
            public string Phase { get; private set; } = "Validating";
            public Texture2D PreviewTexture { get; private set; }
            public bool Cancelled { get; private set; }
            readonly TerrainColorMapBakeAsset recipe;
            readonly bool preview;
            IEnumerator routine;
            RenderTexture target;
            RenderTexture detailAccum;
            RenderTexture detailTarget;
            ComputeShader shader;
            Texture2D staging;
            Texture2D detailStaging;
            Texture2D geometryNormals;
            Texture2D neutralNormal;
            AsyncGPUReadbackRequest request;
            bool requestPending;
            readonly List<Texture> pinned = new List<Texture>();
            readonly Dictionary<Texture2D,Texture> copies = new Dictionary<Texture2D,Texture>();
            internal Operation(TerrainColorMapBakeAsset recipe, bool preview) { this.recipe=recipe; this.preview=preview; }
            internal void Start()
            {
                routine = Run();
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                EditorApplication.quitting += BeforeReload;
            }
            public void Cancel() { Cancelled=true; }
            public void Dispose()
            {
                Cancel();
                if (PreviewTexture) { Object.DestroyImmediate(PreviewTexture); PreviewTexture=null; }
            }
            void BeforeReload()
            {
                Cancel();
                if(requestPending) { request.WaitForCompletion(); requestPending=false; }
                Finish();
            }
            void Tick()
            {
                try
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Terrain Surface Baker", Phase, Phase == "Saving" ? .9f : .4f)) Cancel();
                    if (!routine.MoveNext()) Finish();
                }
                catch (Exception exception) { Error=exception.Message; Finish(); }
            }
            void Finish()
            {
                if(requestPending) { request.WaitForCompletion(); requestPending=false; }
                (routine as IDisposable)?.Dispose(); routine=null;
                if(target) { target.Release(); Object.DestroyImmediate(target); }
                if(detailAccum) { detailAccum.Release(); Object.DestroyImmediate(detailAccum); }
                if(detailTarget) { detailTarget.Release(); Object.DestroyImmediate(detailTarget); }
                if(geometryNormals) Object.DestroyImmediate(geometryNormals);
                if(neutralNormal) Object.DestroyImmediate(neutralNormal);
                if(shader) Object.DestroyImmediate(shader);
                if(staging) Object.DestroyImmediate(staging);
                if(detailStaging) Object.DestroyImmediate(detailStaging);
                foreach(var texture in pinned) if(texture) { if(texture is RenderTexture rt) rt.Release(); Object.DestroyImmediate(texture); }
                pinned.Clear(); copies.Clear();
                EditorApplication.update -= Tick;
                AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                EditorApplication.quitting -= BeforeReload;
                EditorUtility.ClearProgressBar(); IsDone=true;
                if (active==this) active=null;
            }
            IEnumerator Run()
            {
                var errors = Validate(recipe, !preview);
                if(errors.Count > 0) throw new InvalidOperationException(string.Join("\n",errors));
                string revision=Fingerprint(recipe);
                int width=preview ? Mathf.Min(recipe.width,256) : recipe.width;
                int height=preview ? Mathf.Min(recipe.height,256) : recipe.height;
                yield return null;
                if(Cancelled) yield break;
                Phase="Rendering";
                shader=Object.Instantiate(LoadShader());
                shader.hideFlags=HideFlags.HideAndDontSave;
                target=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                    { enableRandomWrite=true, hideFlags=HideFlags.HideAndDontSave };
                if(!target.Create()) throw new InvalidOperationException("Could not allocate RGBAFloat target.");
                Render(shader,target,recipe,Pin);
                if (recipe.bakeDetail)
                {
                    Phase="Preparing Detail Normals";
                    var normalPixels=new Color[width*height];
                    for (int y=0;y<height;y++)
                    {
                        float v=(float)y/(height-1);
                        for (int x=0;x<width;x++)
                        {
                            Vector3 n=recipe.terrainData.GetInterpolatedNormal((float)x/(width-1),v);
                            normalPixels[y*width+x]=new Color(n.x,n.y,n.z,1);
                        }
                        if ((y & 31)==31) { yield return null; if(Cancelled) yield break; }
                    }
                    geometryNormals=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true) { hideFlags=HideFlags.HideAndDontSave };
                    geometryNormals.SetPixels(normalPixels); geometryNormals.Apply(false,false);
                    neutralNormal=new Texture2D(1,1,TextureFormat.RGBA32,false,true) { hideFlags=HideFlags.HideAndDontSave };
                    neutralNormal.SetPixel(0,0,new Color(.5f,.5f,.5f,1)); neutralNormal.Apply(false,false);
                    detailAccum=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                        { enableRandomWrite=true, hideFlags=HideFlags.HideAndDontSave };
                    detailTarget=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                        { enableRandomWrite=true, hideFlags=HideFlags.HideAndDontSave };
                    if(!detailAccum.Create() || !detailTarget.Create()) throw new InvalidOperationException("Could not allocate Detail targets.");
                    RenderDetail(shader,detailAccum,detailTarget,geometryNormals,neutralNormal,recipe,Pin);
                }
                yield return null;
                if(Cancelled) yield break;
                Phase="Reading Back";
                Color[] colors;
                if(SystemInfo.supportsAsyncGPUReadback)
                {
                    request=AsyncGPUReadback.Request(target,0,TextureFormat.RGBAFloat); requestPending=true;
                    while(!request.done) yield return null;
                    requestPending=false;
                    if(Cancelled) yield break;
                    if(request.hasError) throw new InvalidOperationException("GPU readback failed.");
                    colors=request.GetData<Color>().ToArray();
                }
                else
                {
                    var previous=RenderTexture.active;
                    var readback=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
                    try { RenderTexture.active=target; readback.ReadPixels(new Rect(0,0,width,height),0,0); colors=readback.GetPixels(); }
                    finally { RenderTexture.active=previous; Object.DestroyImmediate(readback); }
                }
                var encoded=new Color32[colors.Length];
                for(int i=0;i<colors.Length;i++)
                {
                    Color c=colors[i];
                    if(!Finite(c.r) || !Finite(c.g) || !Finite(c.b) || !Finite(c.a) || c.r<0 || c.g<0 || c.b<0 || c.a<0 || c.r>1 || c.g>1 || c.b>1 || c.a>1)
                        throw new InvalidOperationException($"Evaluated surface at ({i%width}, {i/width}) is non-finite or outside PNG range [0,1].");
                    encoded[i]=new Color32(Encode(c.r),Encode(c.g),Encode(c.b),(byte)Mathf.RoundToInt(c.a*255));
                    if((i & 65535)==65535) { yield return null; if(Cancelled) yield break; }
                }
                staging=new Texture2D(width,height,TextureFormat.RGBA32,false,false) { hideFlags=HideFlags.HideAndDontSave };
                staging.SetPixels32(encoded); staging.Apply(false,false);
                Color32[] detailEncoded=null;
                if (recipe.bakeDetail)
                {
                    Phase="Reading Detail";
                    Color[] detailColors;
                    if(SystemInfo.supportsAsyncGPUReadback)
                    {
                        request=AsyncGPUReadback.Request(detailTarget,0,TextureFormat.RGBAFloat); requestPending=true;
                        while(!request.done) yield return null;
                        requestPending=false;
                        if(Cancelled) yield break;
                        if(request.hasError) throw new InvalidOperationException("Detail GPU readback failed.");
                        detailColors=request.GetData<Color>().ToArray();
                    }
                    else
                    {
                        var previous=RenderTexture.active;
                        var readback=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
                        try { RenderTexture.active=detailTarget; readback.ReadPixels(new Rect(0,0,width,height),0,0); detailColors=readback.GetPixels(); }
                        finally { RenderTexture.active=previous; Object.DestroyImmediate(readback); }
                    }
                    detailEncoded=new Color32[detailColors.Length];
                    for(int i=0;i<detailColors.Length;i++)
                    {
                        Color c=detailColors[i];
                        if(!Finite(c.r)||!Finite(c.g)||!Finite(c.b)||!Finite(c.a)||c.r<0||c.g<0||c.b<0||c.a<0||c.r>1||c.g>1||c.b>1||c.a>1)
                            throw new InvalidOperationException($"Detail at ({i%width}, {i/width}) is outside [0,1].");
                        detailEncoded[i]=new Color32((byte)Mathf.RoundToInt(c.r*255),(byte)Mathf.RoundToInt(c.g*255),(byte)Mathf.RoundToInt(c.b*255),255);
                    }
                    detailStaging=new Texture2D(width,height,TextureFormat.RGBA32,false,true) { hideFlags=HideFlags.HideAndDontSave };
                    detailStaging.SetPixels32(detailEncoded); detailStaging.Apply(false,false);
                }
                if(Cancelled) yield break;
                if(Fingerprint(recipe)!=revision) throw new InvalidOperationException("Source or recipe changed during baking. Bake again.");
                if(preview) { PreviewTexture=staging; staging=null; yield break; }
                Phase="Saving";
                Publish(recipe,staging,encoded,detailStaging,detailEncoded,revision);
            }
            Texture Pin(Texture2D texture)
            {
                if (copies.TryGetValue(texture, out var existing)) return existing;
                // Imported RGB24 textures can have a non-constructible graphicsFormat on Metal.
                // Copy through typed GPU loads into linear float storage, including each original mip.
                int residentWidth=Mathf.Max(1,texture.width>>texture.activeMipmapLimit);
                int residentHeight=Mathf.Max(1,texture.height>>texture.activeMipmapLimit);
                int residentMips=Mathf.Max(1,texture.mipmapCount-texture.activeMipmapLimit);
                var copy=new RenderTexture(residentWidth,residentHeight,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite=true, useMipMap=residentMips>1, autoGenerateMips=false,
                    filterMode=texture.filterMode, wrapModeU=texture.wrapModeU, wrapModeV=texture.wrapModeV,
                    anisoLevel=texture.anisoLevel, mipMapBias=texture.mipMapBias, hideFlags=HideFlags.HideAndDontSave
                };
                pinned.Add(copy); copies.Add(texture,copy);
                if(!copy.Create()) throw new InvalidOperationException("Could not allocate source texture snapshot.");
                int kernel=shader.FindKernel("Snapshot"); shader.SetTexture(kernel,"_Source",texture);
                for(int mip=0;mip<residentMips;mip++)
                {
                    int w=Mathf.Max(1,residentWidth>>mip), h=Mathf.Max(1,residentHeight>>mip);
                    shader.SetInt("_CopyWidth",w); shader.SetInt("_CopyHeight",h); shader.SetInt("_CopyMip",mip);
                    shader.SetTexture(kernel,"_Result",copy,mip); shader.Dispatch(kernel,(w+7)/8,(h+7)/8,1);
                }
                return copy;
            }

        }

        static byte Encode(float linear) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(linear)*255),0,255);
        static ComputeShader LoadShader()
        {
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            if(!shader)
            {
                // Embedded package paths and isolated verification hosts may use another root.
                string script=AssetDatabase.GUIDToAssetPath("5cb8bd99c0714f9b91853bdffaac4f83");
                shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(script);
            }
            if(!shader) throw new InvalidOperationException("TerrainColorMapBaker.compute is missing.");
            return shader;
        }
        static void Render(ComputeShader shader, RenderTexture result, TerrainColorMapBakeAsset recipe, Func<Texture2D,Texture> pin)
        {
            var data=recipe.terrainData; var layers=data.terrainLayers;
            shader.SetInt("_Width",result.width); shader.SetInt("_Height",result.height);
            shader.SetInt("_LayerCount",layers.Length);
            shader.SetInt("_ControlWidth",data.alphamapWidth); shader.SetInt("_ControlHeight",data.alphamapHeight);
            shader.SetInt("_HeightBlend",recipe.material && recipe.material.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT") ? 1 : 0);
            shader.SetFloat("_HeightTransition",recipe.material ? recipe.material.GetFloat("_HeightTransition") : 0f);
            shader.SetTexture(0,"_Result",result);
            for(int group=0;group<data.alphamapTextureCount;group++)
            {
                shader.SetInt("_Group",group); shader.SetTexture(0,"_Control",pin(data.GetAlphamapTexture(group)));
                var st=new Vector4[4]; var scale=new Vector4[4]; var maskScale=new Vector4[4]; var maskOffset=new Vector4[4]; var smoothness=Vector4.zero;
                var hasMask=Vector4.zero;
                for(int i=0;i<4;i++)
                {
                    int index=group*4+i; var layer=index<layers.Length ? layers[index] : null;
                    shader.SetTexture(0,"_Diffuse"+i,layer ? pin(layer.diffuseTexture) : Texture2D.whiteTexture);
                    shader.SetTexture(0,"_Mask"+i,layer && layer.maskMapTexture ? pin(layer.maskMapTexture) : Texture2D.grayTexture);
                    if(!layer) { scale[i]=Vector4.one; continue; }
                    st[i]=new Vector4(data.size.x/layer.tileSize.x,data.size.z/layer.tileSize.y,layer.tileOffset.x/layer.tileSize.x,layer.tileOffset.y/layer.tileSize.y);
                    scale[i]=layer.diffuseRemapMax-layer.diffuseRemapMin;
                    smoothness[i]=Mathf.Max(GraphicsFormatUtility.HasAlphaChannel(layer.diffuseTexture.graphicsFormat) ? 1f : 0f, layer.smoothness);
                    maskScale[i]=layer.maskMapRemapMax-layer.maskMapRemapMin; maskOffset[i]=layer.maskMapRemapMin;
                    hasMask[i]=layer.maskMapTexture ? 1 : 0;
                }
                shader.SetVectorArray("_ST",st); shader.SetVectorArray("_DiffuseScale",scale);
                shader.SetVectorArray("_MaskScale",maskScale); shader.SetVectorArray("_MaskOffset",maskOffset); shader.SetVector("_HasMask",hasMask);
                shader.SetVector("_Smoothness",smoothness);
                shader.Dispatch(0,(result.width+7)/8,(result.height+7)/8,1);
            }
        }

        static void RenderDetail(ComputeShader shader, RenderTexture accum, RenderTexture output, Texture2D geometry, Texture2D neutralNormal,
            TerrainColorMapBakeAsset recipe, Func<Texture2D,Texture> pin)
        {
            var data=recipe.terrainData; var layers=data.terrainLayers;
            int kernel=shader.FindKernel("BakeDetailGroup");
            shader.SetInt("_Width",output.width); shader.SetInt("_Height",output.height);
            shader.SetInt("_LayerCount",layers.Length);
            shader.SetInt("_ControlWidth",data.alphamapWidth); shader.SetInt("_ControlHeight",data.alphamapHeight);
            shader.SetInt("_HeightBlend",recipe.material && recipe.material.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT") ? 1 : 0);
            shader.SetFloat("_HeightTransition",recipe.material ? recipe.material.GetFloat("_HeightTransition") : 0f);
            shader.SetTexture(kernel,"_DetailAccum",accum);
            for(int group=0;group<data.alphamapTextureCount;group++)
            {
                shader.SetInt("_Group",group); shader.SetTexture(kernel,"_Control",pin(data.GetAlphamapTexture(group)));
                var st=new Vector4[4]; var scale=new Vector4[4]; var maskScale=new Vector4[4]; var maskOffset=new Vector4[4];
                var normalScale=Vector4.one; var hasMask=Vector4.zero;
                for(int i=0;i<4;i++)
                {
                    int index=group*4+i; var layer=index<layers.Length ? layers[index] : null;
                    shader.SetTexture(kernel,"_Diffuse"+i,layer ? pin(layer.diffuseTexture) : Texture2D.whiteTexture);
                    shader.SetTexture(kernel,"_Mask"+i,layer && layer.maskMapTexture ? pin(layer.maskMapTexture) : Texture2D.grayTexture);
                    shader.SetTexture(kernel,"_Normal"+i,layer && layer.normalMapTexture ? pin(layer.normalMapTexture) : neutralNormal);
                    if(!layer) { scale[i]=Vector4.one; continue; }
                    st[i]=new Vector4(data.size.x/layer.tileSize.x,data.size.z/layer.tileSize.y,layer.tileOffset.x/layer.tileSize.x,layer.tileOffset.y/layer.tileSize.y);
                    scale[i]=layer.diffuseRemapMax-layer.diffuseRemapMin;
                    maskScale[i]=layer.maskMapRemapMax-layer.maskMapRemapMin; maskOffset[i]=layer.maskMapRemapMin;
                    normalScale[i]=layer.normalScale;
                    hasMask[i]=layer.maskMapTexture ? 1 : 0;
                }
                shader.SetVectorArray("_ST",st); shader.SetVectorArray("_DiffuseScale",scale);
                shader.SetVectorArray("_MaskScale",maskScale); shader.SetVectorArray("_MaskOffset",maskOffset);
                shader.SetVector("_HasMask",hasMask); shader.SetVector("_NormalScale",normalScale);
                shader.Dispatch(kernel,(output.width+7)/8,(output.height+7)/8,1);
            }
            int encode=shader.FindKernel("EncodeDetail");
            shader.SetTexture(encode,"_DetailAccum",accum);
            shader.SetTexture(encode,"_GeometryNormal",geometry);
            shader.SetTexture(encode,"_DetailOutput",output);
            shader.Dispatch(encode,(output.width+7)/8,(output.height+7)/8,1);
        }

        static List<string> PlatformErrors(TextureImporter importer,int width,int height)
        {
            var errors=new List<string>();
            if(!importer) return errors;
            foreach(string platform in Platforms)
            {
                var setting=importer.GetPlatformTextureSettings(platform);
                if(setting.overridden && (setting.maxTextureSize<Mathf.Max(width,height) ||
                    setting.textureCompression!=TextureImporterCompression.Uncompressed || setting.format!=TextureImporterFormat.RGBA32))
                    errors.Add($"Platform override '{setting.name}' changes output size/compression. Adjust it explicitly before rebaking.");
            }
            return errors;
        }

        static bool ImportMatches(TextureImporter importer, int width, int height, Texture2D texture)
        {
            return importer && texture && texture.width==width && texture.height==height && importer.textureType==TextureImporterType.Default &&
                importer.sRGBTexture && importer.ignoreMipmapLimit && importer.mipmapEnabled && !importer.isReadable && importer.wrapMode==TextureWrapMode.Clamp &&
                importer.filterMode==FilterMode.Bilinear && importer.textureCompression==TextureImporterCompression.Uncompressed &&
                !importer.alphaIsTransparency && importer.maxTextureSize>=Mathf.Max(width,height);
        }
        static bool DetailImportMatches(TextureImporter importer, int width, int height, Texture2D texture)
        {
            return importer && texture && texture.width==width && texture.height==height && importer.textureType==TextureImporterType.Default &&
                !importer.sRGBTexture && importer.ignoreMipmapLimit && !importer.mipmapEnabled && !importer.isReadable &&
                importer.wrapMode==TextureWrapMode.Clamp && importer.filterMode==FilterMode.Point &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && !importer.alphaIsTransparency &&
                importer.maxTextureSize>=Mathf.Max(width,height);
        }
        static void VerifyImportedSamples(Texture2D texture, Color32[] expected, int width, int height, bool linear = false)
        {
            var shader=Object.Instantiate(LoadShader());
            var probe=new RenderTexture(3,3,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear) { enableRandomWrite=true };
            Texture2D readback=null;
            try
            {
                if(!probe.Create()) throw new InvalidOperationException("Could not allocate imported-output verification target.");
                int kernel=shader.FindKernel("VerifyOutput");
                shader.SetTexture(kernel,"_Imported",texture); shader.SetTexture(kernel,"_Result",probe);
                shader.SetInt("_Width",width); shader.SetInt("_Height",height); shader.Dispatch(kernel,1,1,1);
                Color[] samples;
                if(SystemInfo.supportsAsyncGPUReadback)
                {
                    var request=AsyncGPUReadback.Request(probe,0,TextureFormat.RGBAFloat); request.WaitForCompletion();
                    if(request.hasError) throw new InvalidOperationException("Imported-output GPU verification failed.");
                    samples=request.GetData<Color>().ToArray();
                }
                else
                {
                    var previous=RenderTexture.active;
                    try { readback=new Texture2D(3,3,TextureFormat.RGBAFloat,false,true); RenderTexture.active=probe; readback.ReadPixels(new Rect(0,0,3,3),0,0); samples=readback.GetPixels(); }
                    finally { RenderTexture.active=previous; }
                }
                for(int y=0;y<3;y++) for(int x=0;x<3;x++)
                {
                    Color c=samples[y*3+x]; Color32 e=expected[(y*(height-1)/2)*width+x*(width-1)/2];
                    if(!Finite(c.r) || !Finite(c.g) || !Finite(c.b) || !Finite(c.a) ||
                        Mathf.Abs((linear ? c.r*255 : Encode(c.r))-e.r)>2 ||
                        Mathf.Abs((linear ? c.g*255 : Encode(c.g))-e.g)>2 ||
                        Mathf.Abs((linear ? c.b*255 : Encode(c.b))-e.b)>2 || Mathf.Abs(c.a*255-e.a)>2)
                        throw new InvalidOperationException("Imported-output sample/color-space verification failed.");
                }
            }
            finally { if(readback) Object.DestroyImmediate(readback); probe.Release(); Object.DestroyImmediate(probe); Object.DestroyImmediate(shader); }
        }
        static void VerifyDetailSamples(Texture2D texture, Color32[] expected, int width, int height)
            => VerifyImportedSamples(texture, expected, width, height, true);

        static void Publish(TerrainColorMapBakeAsset recipe, Texture2D staging, Color32[] expected,
            Texture2D detailStaging, Color32[] detailExpected, string revision)
        {
            var errors=Validate(recipe);
            if(errors.Count>0) throw new InvalidOperationException(string.Join("\n",errors));
            string path=OutputPath(recipe), temp=Path.Combine(Path.GetTempPath(),"eastudio-colormap-"+Guid.NewGuid().ToString("N")+".png");
            string detailPath=recipe.bakeDetail ? DetailPath(recipe) : null;
            string detailTemp=recipe.bakeDetail ? Path.Combine(Path.GetTempPath(),"eastudio-detail-"+Guid.NewGuid().ToString("N")+".png") : null;
            bool existed=File.Exists(path), metaExisted=File.Exists(path+".meta");
            bool detailExisted=detailPath!=null && File.Exists(detailPath), detailMetaExisted=detailPath!=null && File.Exists(detailPath+".meta");
            byte[] oldBytes=existed ? File.ReadAllBytes(path) : null;
            byte[] oldMeta=metaExisted ? File.ReadAllBytes(path+".meta") : null;
            byte[] oldDetailBytes=detailExisted ? File.ReadAllBytes(detailPath) : null;
            byte[] oldDetailMeta=detailMetaExisted ? File.ReadAllBytes(detailPath+".meta") : null;
            string oldRecipe=EditorJsonUtility.ToJson(recipe);
            bool replacing=false;
            Texture2D decoded=null;
            Texture2D detailDecoded=null;
            try
            {
                File.WriteAllBytes(temp,staging.EncodeToPNG());
                decoded=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                if(!ImageConversion.LoadImage(decoded,File.ReadAllBytes(temp)) || decoded.width!=staging.width || decoded.height!=staging.height)
                    throw new InvalidOperationException("Staged PNG failed its dimension/decode check.");
                var pixels=decoded.GetPixels32();
                for(int i=0;i<pixels.Length;i++) if(!pixels[i].Equals(expected[i])) throw new InvalidOperationException("PNG round-trip altered color bytes.");
                if(recipe.bakeDetail)
                {
                    if(!detailStaging || detailExpected==null) throw new InvalidOperationException("Detail output was not staged.");
                    File.WriteAllBytes(detailTemp,detailStaging.EncodeToPNG());
                    detailDecoded=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                    if(!ImageConversion.LoadImage(detailDecoded,File.ReadAllBytes(detailTemp)) || detailDecoded.width!=staging.width || detailDecoded.height!=staging.height)
                        throw new InvalidOperationException("Staged Detail PNG failed its dimension/decode check.");
                    var detailPixels=detailDecoded.GetPixels32();
                    for(int i=0;i<detailPixels.Length;i++) if(!detailPixels[i].Equals(detailExpected[i])) throw new InvalidOperationException("Detail PNG round-trip altered bytes.");
                }
                if(Fingerprint(recipe)!=revision) throw new InvalidOperationException("Source changed before publication. Bake again.");
                replacing=true; File.Copy(temp,path,true);
                if(recipe.bakeDetail) File.Copy(detailTemp,detailPath,true);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(!importer) throw new InvalidOperationException("Output did not import as a texture.");
                var platformErrors=PlatformErrors(importer,staging.width,staging.height);
                if(platformErrors.Count>0) throw new InvalidOperationException(string.Join("\n",platformErrors));
                importer.textureType=TextureImporterType.Default; importer.sRGBTexture=true;
                importer.wrapMode=TextureWrapMode.Clamp; importer.filterMode=FilterMode.Bilinear;
                importer.mipmapEnabled=true; importer.ignoreMipmapLimit=true; importer.isReadable=false; importer.alphaIsTransparency=false;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=Mathf.Max(staging.width,staging.height);
                importer.npotScale=TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(!ImportMatches(importer,staging.width,staging.height,texture)) throw new InvalidOperationException("Imported texture failed the output contract.");
                VerifyImportedSamples(texture,expected,staging.width,staging.height);
                Texture2D detailTexture=null;
                string detailGuid=null;
                if(recipe.bakeDetail)
                {
                    AssetDatabase.ImportAsset(detailPath,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var detailImporter=AssetImporter.GetAtPath(detailPath) as TextureImporter;
                    if(!detailImporter) throw new InvalidOperationException("Detail did not import as a texture.");
                    var detailErrors=PlatformErrors(detailImporter,staging.width,staging.height);
                    if(detailErrors.Count>0) throw new InvalidOperationException(string.Join("\n",detailErrors));
                    detailImporter.textureType=TextureImporterType.Default; detailImporter.sRGBTexture=false;
                    detailImporter.wrapMode=TextureWrapMode.Clamp; detailImporter.filterMode=FilterMode.Point;
                    detailImporter.mipmapEnabled=false; detailImporter.ignoreMipmapLimit=true; detailImporter.isReadable=false;
                    detailImporter.alphaIsTransparency=false; detailImporter.alphaSource=TextureImporterAlphaSource.FromInput;
                    detailImporter.textureCompression=TextureImporterCompression.Uncompressed;
                    detailImporter.maxTextureSize=Mathf.Max(staging.width,staging.height);
                    detailImporter.npotScale=TextureImporterNPOTScale.None;
                    detailImporter.SaveAndReimport();
                    detailTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(detailPath);
                    if(!DetailImportMatches(detailImporter,staging.width,staging.height,detailTexture)) throw new InvalidOperationException("Imported Detail failed its output contract.");
                    VerifyDetailSamples(detailTexture,detailExpected,staging.width,staging.height);
                    detailGuid=AssetDatabase.AssetPathToGUID(detailPath);
                    if(detailExisted && detailGuid!=recipe.detailGuid) throw new InvalidOperationException("Rebake changed the Detail GUID.");
                }
                string guid=AssetDatabase.AssetPathToGUID(path);
                if(existed && guid!=recipe.outputGuid) throw new InvalidOperationException("Rebake changed the output GUID.");
                recipe.outputTexture=texture; recipe.outputGuid=guid;
                if(recipe.bakeDetail) { recipe.detailTexture=detailTexture; recipe.detailGuid=detailGuid; }
                recipe.outputWidth=staging.width; recipe.outputHeight=staging.height;
                recipe.sourceSizeXZ=new Vector2(recipe.terrainData.size.x,recipe.terrainData.size.z);
                recipe.coordinateVersion=1; recipe.encoding="Surface: sRGB8 RGB, linear8 smoothness; optional linear8 Detail / endpoint XZ";
                recipe.bakerVersion=Version; recipe.fingerprint=revision;
                recipe.completedUtc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
                EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssetIfDirty(recipe);
            }
            catch(Exception failure)
            {
                try
                {
                    if(replacing)
                    {
                        if(existed) File.WriteAllBytes(path,oldBytes); else if(File.Exists(path)) File.Delete(path);
                        if(metaExisted) File.WriteAllBytes(path+".meta",oldMeta); else if(File.Exists(path+".meta")) File.Delete(path+".meta");
                        if(detailPath!=null)
                        {
                            if(detailExisted) File.WriteAllBytes(detailPath,oldDetailBytes); else if(File.Exists(detailPath)) File.Delete(detailPath);
                            if(detailMetaExisted) File.WriteAllBytes(detailPath+".meta",oldDetailMeta); else if(File.Exists(detailPath+".meta")) File.Delete(detailPath+".meta");
                        }
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        if(existed) AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        EditorJsonUtility.FromJsonOverwrite(oldRecipe,recipe);
                        EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssetIfDirty(recipe);
                    }
                }
                catch(Exception rollback) { throw new IOException(failure.Message+" Rollback also failed: "+rollback.Message,failure); }
                throw;
            }
            finally
            {
                if(decoded) Object.DestroyImmediate(decoded);
                if(detailDecoded) Object.DestroyImmediate(detailDecoded);
                if(File.Exists(temp)) File.Delete(temp);
                if(detailTemp!=null && File.Exists(detailTemp)) File.Delete(detailTemp);
            }
        }
    }
}
