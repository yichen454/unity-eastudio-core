using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.Editor
{
    public class TextureEditorWindow : EditorWindow
    {
        // ─── Enums ────────────────────────────────────────────────────────────
        private enum OutputFormat { PNG, TGA }
        private enum SourceChannel { R = 0, G = 1, B = 2, A = 3, Luminance = 4 }
        private enum NoiseType { Perlin = 0, Simplex = 1, Worley = 2, White = 3, FBm = 4, Turbulence = 5 }
        private enum OutputResolution { R128 = 128, R256 = 256, R512 = 512, R1024 = 1024, R2048 = 2048, R4096 = 4096 }

        // ─── Channel merge state ──────────────────────────────────────────────
        private struct ChannelSource
        {
            public Texture2D tex;
            public SourceChannel srcChannel;
            public bool invert;
            public float constantValue;
        }

        private ChannelSource _chanR = new ChannelSource { srcChannel = SourceChannel.R, constantValue = 0f };
        private ChannelSource _chanG = new ChannelSource { srcChannel = SourceChannel.G, constantValue = 0f };
        private ChannelSource _chanB = new ChannelSource { srcChannel = SourceChannel.B, constantValue = 0f };
        private ChannelSource _chanA = new ChannelSource { srcChannel = SourceChannel.A, constantValue = 1f };

        private OutputResolution _mergeResolution = OutputResolution.R512;
        private OutputFormat     _mergeFormat     = OutputFormat.PNG;
        private string           _mergeOutputPath = "";
        private string           _mergeFileName   = "MaskMap";
        private bool             _mergeBusy;

        // ─── Noise gen state ──────────────────────────────────────────────────
        private NoiseType        _noiseType       = NoiseType.Perlin;
        private OutputResolution _noiseResolution = OutputResolution.R512;
        private float            _noiseScale      = 4f;
        private int              _noiseOctaves    = 4;
        private float            _noisePersistence = 0.5f;
        private float            _noiseLacunarity  = 2f;
        private int              _noiseSeed        = 0;
        private bool             _noiseInvert;
        private bool             _noiseTiling;
        private OutputFormat     _noiseFormat     = OutputFormat.PNG;
        private string           _noiseOutputPath = "Assets/";
        private string           _noiseFileName   = "Noise_Perlin";
        private bool             _noiseBusy;
        private double           _noiseDirtyTime  = -1;
        private const double     PreviewDebounce  = 0.3;
        private Texture2D        _previewTex;
        private const int        PreviewSize      = 128;
        private RenderTexture    _previewRT;

        // ─── Compute shaders ──────────────────────────────────────────────────
        private ComputeShader _noiseShader;
        private ComputeShader _mergeShader;

        // ─── Tab ──────────────────────────────────────────────────────────────
        private int _tabIndex;
        private static readonly string[] Tabs = { "通道合成", "噪声生成" };

        // ─── Scroll ───────────────────────────────────────────────────────────
        private Vector2 _mergeScroll;
        private Vector2 _noiseScroll;

        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Tools/EAStudio/贴图/贴图编辑器")]
        public static void Open()
        {
            var win = GetWindow<TextureEditorWindow>("贴图编辑器");
            win.minSize = new Vector2(440, 560);
        }

        private void OnEnable()
        {
            LoadShaders();
        }

        private void OnDisable()
        {
            ReleasePreview();
        }

        private void LoadShaders()
        {
            // Find compute shaders anywhere under Editor/
            string[] guids = AssetDatabase.FindAssets("t:ComputeShader NoiseGen",
                new[] { "Packages/com.eastudio.core/Editor" });
            if (guids.Length > 0)
                _noiseShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));

            guids = AssetDatabase.FindAssets("t:ComputeShader ChannelMerge",
                new[] { "Packages/com.eastudio.core/Editor" });
            if (guids.Length > 0)
                _mergeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void ReleasePreview()
        {
            if (_previewRT != null) { _previewRT.Release(); _previewRT = null; }
            if (_previewTex != null) { DestroyImmediate(_previewTex); _previewTex = null; }
        }

        // ─────────────────────────────────────────────────────────────────────
        private void Update()
        {
            // Debounced preview refresh for noise tab
            if (_noiseDirtyTime > 0 && EditorApplication.timeSinceStartup - _noiseDirtyTime >= PreviewDebounce)
            {
                _noiseDirtyTime = -1;
                DispatchNoisePreview();
            }
        }

        private void OnGUI()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                EditorGUILayout.HelpBox("当前设备不支持 Compute Shader，贴图编辑器无法使用。", MessageType.Error);
                return;
            }
            if (_noiseShader == null || _mergeShader == null)
            {
                EditorGUILayout.HelpBox("未找到 Compute Shader（NoiseGen / ChannelMerge）。请检查包目录。", MessageType.Error);
                if (GUILayout.Button("重新加载")) LoadShaders();
                return;
            }

            _tabIndex = GUILayout.Toolbar(_tabIndex, Tabs);
            EditorGUILayout.Space(4);

            if (_tabIndex == 0)
                DrawMergePanel();
            else
                DrawNoisePanel();
        }

        // ═════════════════════════════════════════════════════════════════════
        // PANEL 1 — Channel Merge
        // ═════════════════════════════════════════════════════════════════════
        private void DrawMergePanel()
        {
            _mergeScroll = EditorGUILayout.BeginScrollView(_mergeScroll);

            EditorGUILayout.HelpBox("HDRP Mask Map 标准：R=Metallic  G=AO  B=Detail Mask  A=Smoothness",
                MessageType.Info);
            EditorGUILayout.Space(4);

            DrawChannelField("R 通道 (Metallic)", ref _chanR);
            DrawChannelField("G 通道 (AO)",       ref _chanG);
            DrawChannelField("B 通道 (Detail)",   ref _chanB);
            DrawChannelField("A 通道 (Smooth)",   ref _chanA);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输出设置", EditorStyles.boldLabel);

            _mergeResolution = (OutputResolution)EditorGUILayout.EnumPopup("分辨率", _mergeResolution);
            _mergeFormat     = (OutputFormat)EditorGUILayout.EnumPopup("格式", _mergeFormat);

            EditorGUILayout.BeginHorizontal();
            _mergeOutputPath = EditorGUILayout.TextField("输出目录", _mergeOutputPath);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                string picked = EditorUtility.OpenFolderPanel("选择输出目录", "Assets", "");
                if (!string.IsNullOrEmpty(picked))
                    _mergeOutputPath = FileSystemToAssetPath(picked);
            }
            if (GUILayout.Button("自动", GUILayout.Width(40)))
                _mergeOutputPath = DefaultMergePath();
            EditorGUILayout.EndHorizontal();

            _mergeFileName = EditorGUILayout.TextField("文件名", _mergeFileName);

            string fullPreview = $"{_mergeOutputPath}/{_mergeFileName}.{_mergeFormat.ToString().ToLower()}";
            EditorGUILayout.LabelField("完整路径", fullPreview, EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(_mergeBusy))
            {
                if (GUILayout.Button("生成合成贴图", GUILayout.Height(32)))
                    ExecuteMerge();
            }
            if (_mergeBusy)
                EditorGUILayout.HelpBox("GPU 正在处理中…", MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawChannelField(string label, ref ChannelSource ch)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            ch.tex = (Texture2D)EditorGUILayout.ObjectField(
                "源贴图", ch.tex, typeof(Texture2D), false);

            using (new EditorGUI.DisabledScope(ch.tex != null))
                ch.constantValue = EditorGUILayout.Slider("常数值", ch.constantValue, 0f, 1f);

            using (new EditorGUI.DisabledScope(ch.tex == null))
                ch.srcChannel = (SourceChannel)EditorGUILayout.EnumPopup("采样通道", ch.srcChannel);

            ch.invert = EditorGUILayout.Toggle("反转", ch.invert);
            EditorGUILayout.EndVertical();
        }

        private string DefaultMergePath()
        {
            if (_chanR.tex != null)
            {
                string p = AssetDatabase.GetAssetPath(_chanR.tex);
                if (!string.IsNullOrEmpty(p))
                    return Path.GetDirectoryName(p).Replace("\\", "/");
            }
            return "Assets";
        }

        // ═════════════════════════════════════════════════════════════════════
        // PANEL 2 — Noise Generation
        // ═════════════════════════════════════════════════════════════════════
        private void DrawNoisePanel()
        {
            _noiseScroll = EditorGUILayout.BeginScrollView(_noiseScroll);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("噪声参数", EditorStyles.boldLabel);
            _noiseType       = (NoiseType)EditorGUILayout.EnumPopup("噪声类型", _noiseType);
            _noiseResolution = (OutputResolution)EditorGUILayout.EnumPopup("分辨率", _noiseResolution);
            _noiseScale      = EditorGUILayout.FloatField("缩放 (Scale)", _noiseScale);
            _noiseSeed       = EditorGUILayout.IntField("随机种子 (Seed)", _noiseSeed);
            _noiseInvert     = EditorGUILayout.Toggle("反转", _noiseInvert);
            _noiseTiling     = EditorGUILayout.Toggle("无缝平铺 (Tiling)", _noiseTiling);

            bool isFractal = (_noiseType == NoiseType.FBm || _noiseType == NoiseType.Turbulence);
            if (isFractal)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("分形参数", EditorStyles.miniBoldLabel);
                _noiseOctaves     = EditorGUILayout.IntSlider("Octaves", _noiseOctaves, 1, 8);
                _noisePersistence = EditorGUILayout.Slider("Persistence", _noisePersistence, 0f, 1f);
                _noiseLacunarity  = EditorGUILayout.Slider("Lacunarity", _noiseLacunarity, 1f, 4f);
            }

            bool changed = EditorGUI.EndChangeCheck();
            if (changed) MarkNoiseDirty();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输出设置", EditorStyles.boldLabel);
            _noiseFormat   = (OutputFormat)EditorGUILayout.EnumPopup("格式", _noiseFormat);

            EditorGUILayout.BeginHorizontal();
            _noiseOutputPath = EditorGUILayout.TextField("输出目录", _noiseOutputPath);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                string picked = EditorUtility.OpenFolderPanel("选择输出目录", "Assets", "");
                if (!string.IsNullOrEmpty(picked))
                    _noiseOutputPath = FileSystemToAssetPath(picked);
            }
            EditorGUILayout.EndHorizontal();

            _noiseFileName = EditorGUILayout.TextField("文件名", _noiseFileName);
            string fullPreview2 = $"{_noiseOutputPath}/{_noiseFileName}.{_noiseFormat.ToString().ToLower()}";
            EditorGUILayout.LabelField("完整路径", fullPreview2, EditorStyles.miniLabel);

            EditorGUILayout.Space(6);

            // Preview
            EditorGUILayout.LabelField("预览 (128×128)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新预览", GUILayout.Width(90)))
                DispatchNoisePreview();
            EditorGUILayout.EndHorizontal();

            if (_previewTex != null)
            {
                Rect previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize,
                    GUILayout.ExpandWidth(false));
                GUI.DrawTexture(previewRect, _previewTex, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(_noiseBusy))
            {
                if (GUILayout.Button("生成并保存", GUILayout.Height(32)))
                    ExecuteNoiseSave();
            }
            if (_noiseBusy)
                EditorGUILayout.HelpBox("GPU 正在处理中…", MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void MarkNoiseDirty()
        {
            _noiseDirtyTime = EditorApplication.timeSinceStartup;
        }

        // ═════════════════════════════════════════════════════════════════════
        // GPU Dispatch — Noise Preview
        // ═════════════════════════════════════════════════════════════════════
        private void DispatchNoisePreview()
        {
            if (_noiseShader == null || _noiseBusy) return;

            var rt = GetOrCreateRT(ref _previewRT, PreviewSize, RenderTextureFormat.ARGBFloat);
            DispatchNoise(rt, PreviewSize);

            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat, req =>
            {
                if (req.hasError) { Debug.LogWarning("[TextureEditor] Preview readback error."); return; }

                if (_previewTex == null || _previewTex.width != PreviewSize)
                {
                    if (_previewTex != null) DestroyImmediate(_previewTex);
                    _previewTex = new Texture2D(PreviewSize, PreviewSize,
                        TextureFormat.RGBAFloat, false, true);
                }
                _previewTex.SetPixelData(req.GetData<float>(), 0);
                _previewTex.Apply();
                Repaint();
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        // GPU Dispatch — Noise Save
        // ═════════════════════════════════════════════════════════════════════
        private void ExecuteNoiseSave()
        {
            if (_noiseShader == null || _noiseBusy) return;

            int res = (int)_noiseResolution;
            var rt  = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.Create();

            DispatchNoise(rt, res);
            _noiseBusy = true;

            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat, req =>
            {
                _noiseBusy = false;
                rt.Release();
                if (req.hasError) { Debug.LogError("[TextureEditor] Noise readback error."); return; }

                var tex = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
                tex.SetPixelData(req.GetData<float>(), 0);
                tex.Apply();
                SaveAndImport(tex, _noiseOutputPath, _noiseFileName, _noiseFormat);
                DestroyImmediate(tex);
                Repaint();
            });
        }

        private void DispatchNoise(RenderTexture rt, int res)
        {
            int kernel = _noiseShader.FindKernel("CSMain");
            _noiseShader.SetTexture(kernel, "Result", rt);
            _noiseShader.SetInt("NoiseType",  (int)_noiseType);
            _noiseShader.SetInt("Resolution", res);
            _noiseShader.SetFloat("Scale",       Mathf.Max(0.001f, _noiseScale));
            _noiseShader.SetInt("Octaves",       _noiseOctaves);
            _noiseShader.SetFloat("Persistence", _noisePersistence);
            _noiseShader.SetFloat("Lacunarity",  _noiseLacunarity);
            _noiseShader.SetFloat("Seed",        _noiseSeed);
            _noiseShader.SetInt("Invert",        _noiseInvert ? 1 : 0);
            _noiseShader.SetInt("Tiling",        _noiseTiling ? 1 : 0);

            int groups = Mathf.CeilToInt(res / 8f);
            _noiseShader.Dispatch(kernel, groups, groups, 1);
        }

        // ═════════════════════════════════════════════════════════════════════
        // GPU Dispatch — Channel Merge
        // ═════════════════════════════════════════════════════════════════════
        private void ExecuteMerge()
        {
            if (_mergeShader == null || _mergeBusy) return;

            // Auto-infer output resolution from largest input
            int res = (int)_mergeResolution;
            int autoRes = BestResolution(_chanR.tex, _chanG.tex, _chanB.tex, _chanA.tex);
            if (autoRes > 0) res = autoRes;

            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.Create();

            int kernel = _mergeShader.FindKernel("CSMain");
            _mergeShader.SetTexture(kernel, "Result", rt);
            _mergeShader.SetInt("Resolution", res);

            BindChannel(kernel, "TexR", "HasTexR", "SrcChanR", "InvertR", "ConstR", _chanR);
            BindChannel(kernel, "TexG", "HasTexG", "SrcChanG", "InvertG", "ConstG", _chanG);
            BindChannel(kernel, "TexB", "HasTexB", "SrcChanB", "InvertB", "ConstB", _chanB);
            BindChannel(kernel, "TexA", "HasTexA", "SrcChanA", "InvertA", "ConstA", _chanA);

            int groups = Mathf.CeilToInt(res / 8f);
            _mergeShader.Dispatch(kernel, groups, groups, 1);
            _mergeBusy = true;

            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat, req =>
            {
                _mergeBusy = false;
                rt.Release();
                if (req.hasError) { Debug.LogError("[TextureEditor] Merge readback error."); return; }

                var tex = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
                tex.SetPixelData(req.GetData<float>(), 0);
                tex.Apply();

                string outPath = string.IsNullOrEmpty(_mergeOutputPath)
                    ? DefaultMergePath() : _mergeOutputPath;
                SaveAndImport(tex, outPath, _mergeFileName, _mergeFormat, linear: true);
                DestroyImmediate(tex);
                Repaint();
            });
        }

        private void BindChannel(int kernel,
            string texName, string hasName, string chanName, string invName, string constName,
            ChannelSource ch)
        {
            bool hasTex = ch.tex != null;
            if (hasTex)
            {
                // Ensure texture is readable via a temporary RenderTexture blit
                Texture2D readable = EnsureReadable(ch.tex);
                _mergeShader.SetTexture(kernel, texName, readable);
            }
            else
            {
                // Bind a 1×1 placeholder so shader slot is not null
                _mergeShader.SetTexture(kernel, texName, Texture2D.blackTexture);
            }
            _mergeShader.SetInt(hasName,   hasTex ? 1 : 0);
            _mergeShader.SetInt(chanName,  (int)ch.srcChannel);
            _mergeShader.SetInt(invName,   ch.invert ? 1 : 0);
            _mergeShader.SetFloat(constName, ch.constantValue);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static Texture2D EnsureReadable(Texture2D src)
        {
            if (src == null) return null;
            // If already readable, return as-is
            var path = AssetDatabase.GetAssetPath(src);
            if (!string.IsNullOrEmpty(path))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.isReadable)
                    return src;
            }
            // Blit to a temporary RenderTexture and read back
            var tmp = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, tmp);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            var readable = new Texture2D(src.width, src.height, TextureFormat.RGBAFloat, false, true);
            readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            return readable;
        }

        private static RenderTexture GetOrCreateRT(ref RenderTexture rt, int size, RenderTextureFormat fmt)
        {
            if (rt != null && rt.width == size) return rt;
            if (rt != null) rt.Release();
            rt = new RenderTexture(size, size, 0, fmt, RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.Create();
            return rt;
        }

        private static int BestResolution(params Texture2D[] textures)
        {
            int best = 0;
            foreach (var t in textures)
                if (t != null) best = Mathf.Max(best, Mathf.Max(t.width, t.height));
            return best;
        }

        private static void SaveAndImport(Texture2D tex, string assetDir, string fileName, OutputFormat fmt, bool linear = false)
        {
            // Ensure the directory exists in the project
            string absDir = Application.dataPath.Replace("Assets", "") + assetDir;
            if (!Directory.Exists(absDir)) Directory.CreateDirectory(absDir);

            string ext    = fmt == OutputFormat.PNG ? ".png" : ".tga";
            string assetPath = $"{assetDir}/{fileName}{ext}";
            string absPath   = Application.dataPath.Replace("Assets", "") + assetPath;

            byte[] bytes = fmt == OutputFormat.PNG ? tex.EncodeToPNG() : tex.EncodeToTGA();
            File.WriteAllBytes(absPath, bytes);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            if (linear)
            {
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }
            }

            AssetDatabase.Refresh();

            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"[TextureEditor] 已保存：{assetPath}");
            }
        }

        private static string FileSystemToAssetPath(string absPath)
        {
            string dataPath = Application.dataPath.Replace("\\", "/");
            absPath = absPath.Replace("\\", "/");
            if (absPath.StartsWith(dataPath))
                return "Assets" + absPath.Substring(dataPath.Length);
            return absPath;
        }
    }
}
