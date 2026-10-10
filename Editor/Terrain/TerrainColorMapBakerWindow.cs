using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    public sealed class TerrainColorMapBakerWindow : EditorWindow
    {
        static readonly int[] Sizes = {256,512,1024,2048,4096};
        static readonly string[] SizeLabels = {"256","512","1024","2048","4096"};
        [SerializeField] TerrainColorMapBakeAsset recipe;
        [SerializeField] Terrain source;
        TerrainColorMapBakeAsset draft;
        TerrainColorMapBaker.Operation operation;
        List<string> diagnostics = new List<string>();
        string status = "Not Baked";
        string lastError;
        bool completionHandled;
        bool sourceChanged;
        bool refreshingStatus;
        readonly HashSet<int> relevantObjects=new HashSet<int>();
        double lastStatusRefresh;
        Vector2 scroll;

        [MenuItem("Tools/EAStudio/Terrain/Surface Baker")]
        public static void Open() => GetWindow<TerrainColorMapBakerWindow>("Surface Baker");
        public static void Open(TerrainColorMapBakeAsset selected)
        {
            var window=GetWindow<TerrainColorMapBakerWindow>("Surface Baker");
            window.recipe=selected; window.RefreshStatus();
        }
        void OnEnable()
        {
            draft=CreateInstance<TerrainColorMapBakeAsset>(); draft.hideFlags=HideFlags.HideAndDontSave;
            EditorApplication.projectChanged += RefreshStatus;
            Undo.undoRedoPerformed += RefreshStatus;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            TerrainCallbacks.textureChanged += OnTerrainTextureChanged;
            TerrainCallbacks.heightmapChanged += OnTerrainHeightmapChanged;
            RefreshStatus();
        }
        void OnDisable()
        {
            operation?.Dispose(); operation=null;
            EditorApplication.projectChanged -= RefreshStatus;
            Undo.undoRedoPerformed -= RefreshStatus;
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            TerrainCallbacks.textureChanged -= OnTerrainTextureChanged;
            TerrainCallbacks.heightmapChanged -= OnTerrainHeightmapChanged;
            if(draft) DestroyImmediate(draft);
        }
        void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if(refreshingStatus) return;
            for(int i=0;i<stream.length;i++)
            {
                if(stream.GetEventType(i)==ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i,out var change);
                    if(relevantObjects.Contains(change.instanceId)) { sourceChanged=true; return; }
                }
            }
        }
        void Track(UnityEngine.Object value)
        {
            if(!value) return;
            relevantObjects.Add(value.GetInstanceID());
            var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(value));
            if(importer) relevantObjects.Add(importer.GetInstanceID());
        }
        void TrackCurrentSources()
        {
            relevantObjects.Clear(); Track(Current); Track(Current.terrainData); Track(Current.material); Track(Current.outputTexture);
            if(!Current.terrainData) return;
            foreach(var layer in Current.terrainData.terrainLayers)
            {
                Track(layer); if(layer) { Track(layer.diffuseTexture); Track(layer.maskMapTexture); if(Current.bakeDetail) Track(layer.normalMapTexture); }
            }
        }
        void OnTerrainTextureChanged(Terrain terrain,string textureName,RectInt region,bool synched)
        {
            if(!refreshingStatus && Current && terrain.terrainData==Current.terrainData && textureName==TerrainData.AlphamapTextureName) sourceChanged=true;
        }
        void OnTerrainHeightmapChanged(Terrain terrain,RectInt region,bool synched)
        {
            if(!refreshingStatus && Current && Current.bakeDetail && terrain.terrainData==Current.terrainData) sourceChanged=true;
        }
        void OnFocus() { RefreshStatus(); }
        void OnSelectionChange()
        {
            if(Selection.activeObject is TerrainColorMapBakeAsset selected) { recipe=selected; RefreshStatus(); Repaint(); }
        }
        TerrainColorMapBakeAsset Current => recipe ? recipe : draft;
        void RefreshStatus()
        {
            if(!Current || TerrainColorMapBaker.IsBusy) return;
            sourceChanged=false; lastStatusRefresh=EditorApplication.timeSinceStartup;
            try { refreshingStatus=true; TrackCurrentSources(); status=TerrainColorMapBaker.GetStatus(Current); diagnostics=TerrainColorMapBaker.Validate(Current,recipe); }
            catch(Exception error) { status="Invalid Source"; diagnostics=new List<string>{error.Message}; }
            finally { refreshingStatus=false; }
            Repaint();
        }
        void Update()
        {
            if(sourceChanged && !TerrainColorMapBaker.IsBusy && EditorApplication.timeSinceStartup-lastStatusRefresh>.5) RefreshStatus();
            if(operation==null || completionHandled) return;
            Repaint();
            if(!operation.IsDone) return;
            completionHandled=true;
            lastError=operation.Error ?? (operation.Cancelled ? "Operation cancelled. Previous result retained." : null);
            RefreshStatus();
        }
        void OnGUI()
        {
            if(!Current) return;
            scroll=EditorGUILayout.BeginScrollView(scroll);
            using(new EditorGUI.DisabledScope(TerrainColorMapBaker.IsBusy))
            {
                EditorGUILayout.LabelField("Source",EditorStyles.boldLabel);
                var selected=(TerrainColorMapBakeAsset)EditorGUILayout.ObjectField("Recipe",recipe,typeof(TerrainColorMapBakeAsset),false);
                if(selected!=recipe) { operation?.Dispose(); operation=null; recipe=selected; RefreshStatus(); }
                source=(Terrain)EditorGUILayout.ObjectField("Terrain",source,typeof(Terrain),true);
                if(GUILayout.Button("Use Selected Terrain"))
                {
                    source=Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Terrain>() : null;
                    Extract();
                }
                using(new EditorGUI.DisabledScope(!source)) if(GUILayout.Button("Extract Terrain Source")) Extract();
                EditorGUI.BeginChangeCheck();
                var data=(TerrainData)EditorGUILayout.ObjectField("TerrainData",Current.terrainData,typeof(TerrainData),false);
                var material=(Material)EditorGUILayout.ObjectField("Material (Stock Default if Empty)",Current.material,typeof(Material),false);
                EditorGUILayout.Space(); EditorGUILayout.LabelField("Output",EditorStyles.boldLabel);
                int width=EditorGUILayout.IntPopup("Width",Current.width,SizeLabels,Sizes);
                int height=EditorGUILayout.IntPopup("Height",Current.height,SizeLabels,Sizes);
                bool bakeDetail=EditorGUILayout.Toggle("Bake Detail (Optional)",Current.bakeDetail);
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Current,"Edit Surface Recipe");
                    Current.terrainData=data; Current.material=material; Current.width=width; Current.height=height; Current.bakeDetail=bakeDetail;
                    EditorUtility.SetDirty(Current); RefreshStatus();
                }
                if(Current.terrainData)
                {
                    var size=Current.terrainData.size;
                    EditorGUILayout.LabelField("Local XZ Size",$"{size.x:g6} × {size.z:g6} m");
                    EditorGUILayout.LabelField("Meters per Texel",$"X: {size.x/(width-1):g4}   Z: {size.z/(height-1):g4}");
                    EditorGUILayout.LabelField("Layers",Current.terrainData.terrainLayers.Length.ToString());
                }
                EditorGUILayout.LabelField("Blend",Current.material && Current.material.HasProperty("_EnableHeightBlend") && Current.material.GetFloat("_EnableHeightBlend")>0 ? "Height (only for ≤ 4 layers)" : "Opacity as Density / Control");
                EditorGUILayout.LabelField("Estimated Bake Working Set",$"{(long)width*height*(Current.bakeDetail ? 104 : 36)/(1024f*1024f):N1} MiB + source snapshots / PNG / driver allocations");
                EditorGUILayout.LabelField("Estimated GPU Source Snapshots",$"{SourceSnapshotBytes()/(1024f*1024f):N1} MiB");
                EditorGUILayout.LabelField("Recipe Path",recipe ? AssetDatabase.GetAssetPath(recipe) : "Not saved");
                EditorGUILayout.LabelField("PNG Path",recipe ? TerrainColorMapBaker.OutputPath(recipe) : "Derived when recipe is saved");
                if(Current.bakeDetail) EditorGUILayout.LabelField("Detail Path",recipe ? TerrainColorMapBaker.DetailPath(recipe) : "Derived when recipe is saved");
                if(GUILayout.Button(recipe ? "Save Recipe As..." : "Save Recipe...")) SaveRecipe();
                if(GUILayout.Button("Validate")) { lastError=null; RefreshStatus(); }
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("Preview (Approximate)")) Begin(true);
                    using(new EditorGUI.DisabledScope(!recipe)) if(GUILayout.Button(Current.outputTexture ? "Rebake" : "Bake")) Begin(false);
                }
            }
            if(operation!=null && !operation.IsDone)
            {
                EditorGUILayout.LabelField("Phase",operation.Phase);
                if(GUILayout.Button("Cancel")) operation.Cancel();
            }
            foreach(string error in diagnostics) EditorGUILayout.HelpBox(error,MessageType.Error);
            if(!string.IsNullOrEmpty(lastError)) EditorGUILayout.HelpBox(lastError,MessageType.Error);
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Preview / Result",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source Status",status);
            if(status=="Stale") EditorGUILayout.HelpBox("Source paint, layer settings, texture dependencies, material, dimensions, or output import settings changed. Validate and rebake.",MessageType.Warning);
            if(operation?.PreviewTexture)
            {
                EditorGUILayout.LabelField("Approximate Preview — 256 px maximum per axis"); DrawTexture(operation.PreviewTexture);
            }
            if(Current.outputTexture)
            {
                EditorGUILayout.ObjectField("Last Successful Surface",Current.outputTexture,typeof(Texture2D),false);
                EditorGUILayout.LabelField("Result Dimensions",$"{Current.outputWidth} × {Current.outputHeight}");
                EditorGUILayout.LabelField("Completed (UTC)",Current.completedUtc);
                EditorGUILayout.LabelField("Encoding",Current.encoding);
                DrawTexture(Current.outputTexture);
                if(GUILayout.Button("Select Output Texture")) { Selection.activeObject=Current.outputTexture; EditorGUIUtility.PingObject(Current.outputTexture); }
            }
            if(Current.detailTexture) EditorGUILayout.ObjectField("Last Successful Detail",Current.detailTexture,typeof(Texture2D),false);
            EditorGUILayout.EndScrollView();
        }
        long SourceSnapshotBytes()
        {
            if(!Current.terrainData) return 0;
            var textures=new HashSet<Texture2D>();
            var data=Current.terrainData;
            for(int group=0;group<data.alphamapTextureCount;group++) textures.Add(data.GetAlphamapTexture(group));
            foreach(var layer in data.terrainLayers)
                if(layer) { if(layer.diffuseTexture) textures.Add(layer.diffuseTexture); if(layer.maskMapTexture) textures.Add(layer.maskMapTexture); if(Current.bakeDetail && layer.normalMapTexture) textures.Add(layer.normalMapTexture); }
            long bytes=0;
            foreach(var texture in textures)
            {
                int width=Mathf.Max(1,texture.width>>texture.activeMipmapLimit),height=Mathf.Max(1,texture.height>>texture.activeMipmapLimit);
                for(int mip=0;mip<Mathf.Max(1,texture.mipmapCount-texture.activeMipmapLimit);mip++)
                    bytes+=(long)Mathf.Max(1,width>>mip)*Mathf.Max(1,height>>mip)*16;
            }
            return bytes;
        }
        void DrawTexture(Texture2D texture)
        {
            Rect rect=GUILayoutUtility.GetAspectRect((float)texture.width/texture.height,GUILayout.MaxHeight(400));
            EditorGUI.DrawPreviewTexture(rect,texture,null,ScaleMode.ScaleToFit);
        }
        void Extract()
        {
            Undo.RecordObject(Current,"Extract Terrain Surface Source");
            lastError=TerrainColorMapBaker.Extract(source,Current);
            if(lastError==null) EditorUtility.SetDirty(Current);
            RefreshStatus();
        }
        void SaveRecipe()
        {
            string path=EditorUtility.SaveFilePanelInProject("Save Surface Recipe","Terrain_SurfaceBake","asset","Choose a new recipe path under Assets.");
            if(string.IsNullOrEmpty(path)) return;
            if(!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) { lastError="Recipe path already exists. Choose a new path or reopen the existing recipe."; return; }
            var saved=Instantiate(Current); saved.hideFlags=HideFlags.None;
            saved.outputTexture=null; saved.outputGuid=null; saved.detailTexture=null; saved.detailGuid=null; saved.fingerprint=null; saved.completedUtc=null;
            saved.outputWidth=saved.outputHeight=0;
            AssetDatabase.CreateAsset(saved,path); AssetDatabase.SaveAssetIfDirty(saved);
            recipe=saved; RefreshStatus();
        }
        void Begin(bool preview)
        {
            operation?.Dispose(); operation=null; lastError=null; completionHandled=false;
            try { operation=preview ? TerrainColorMapBaker.Preview(Current) : TerrainColorMapBaker.Bake(Current); }
            catch(Exception error) { lastError=error.Message; }
        }
    }

    [CustomEditor(typeof(TerrainColorMapBakeAsset))]
    public sealed class TerrainColorMapBakeAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if(GUILayout.Button("Open Surface Baker")) TerrainColorMapBakerWindow.Open((TerrainColorMapBakeAsset)target);
        }
        [UnityEditor.Callbacks.OnOpenAsset]
        static bool OnOpenAsset(int instanceId,int line)
        {
            if(EditorUtility.InstanceIDToObject(instanceId) is TerrainColorMapBakeAsset recipe) { TerrainColorMapBakerWindow.Open(recipe); return true; }
            return false;
        }
    }
}
