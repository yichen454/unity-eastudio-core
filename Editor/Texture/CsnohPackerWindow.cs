using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.Editor
{
    /// <summary>
    /// Packs Color + Normal + AO + Height + Smoothness into the dual-texture
    /// CSNOH format:
    ///   CS  = RGB:Color   A:Smoothness
    ///   NOH = RG:Normal.xy  B:AO  A:Height
    /// </summary>
    public class CsnohPackerWindow : EditorWindow
    {
        // ── Enums ─────────────────────────────────────────────────────────────
        private enum OutputFormat { PNG, TGA }
        private enum SourceChannel { R = 0, G = 1, B = 2, A = 3, Luminance = 4 }
        private enum NormalEncoding { StandardRGB = 0, BC5_DXT5nm = 1 }
        private enum OutputResolution
        {
            R128 = 128, R256 = 256, R512 = 512,
            R1024 = 1024, R2048 = 2048, R4096 = 4096
        }

        // ── Inputs ────────────────────────────────────────────────────────────
        private Texture2D _texColor;
        private Texture2D _texSmooth;
        private Texture2D _texNormal;
        private Texture2D _texAO;
        private Texture2D _texHeight;

        private SourceChannel  _chanSmooth  = SourceChannel.R;
        private SourceChannel  _chanAO      = SourceChannel.R;
        private SourceChannel  _chanHeight  = SourceChannel.R;

        private bool  _invSmooth;
        private bool  _invAO;
        private bool  _invHeight;

        private float _constSmooth = 0.5f;
        private float _constAO    = 1.0f;
        private float _constHeight = 0.5f;

        private NormalEncoding _normalEncoding = NormalEncoding.StandardRGB;

        // ── Output ────────────────────────────────────────────────────────────
        private OutputResolution _resolution = OutputResolution.R1024;
        private OutputFormat     _format     = OutputFormat.PNG;
        private string           _outputPath = "";
        private string           _fileNameCS  = "CSNOH_CS";
        private string           _fileNameNOH = "CSNOH_NOH";

        // ── State ─────────────────────────────────────────────────────────────
        private ComputeShader _shader;
        private bool          _busy;
        private Vector2       _scroll;

        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Tools/EAStudio/贴图/CSNOH 贴图打包")]
        public static void Open()
        {
            var win = GetWindow<CsnohPackerWindow>("CSNOH 贴图打包");
            win.minSize = new Vector2(440, 600);
        }

        private void OnEnable()  => LoadShader();

        private void LoadShader()
        {
            var guids = AssetDatabase.FindAssets("t:ComputeShader CsnohPacker",
                new[] { "Packages/com.eastudio.core/Editor" });
            if (guids.Length > 0)
                _shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ─────────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                EditorGUILayout.HelpBox("当前设备不支持 Compute Shader。", MessageType.Error);
                return;
            }
            if (_shader == null)
            {
                EditorGUILayout.HelpBox("未找到 CsnohPacker.compute，请检查包目录。", MessageType.Error);
                if (GUILayout.Button("重新加载")) LoadShader();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawDiagram();
            EditorGUILayout.Space(6);
            DrawInputs();
            EditorGUILayout.Space(6);
            DrawOutputSettings();
            EditorGUILayout.Space(8);
            DrawGenerateButton();

            EditorGUILayout.EndScrollView();
        }

        // ── Diagram ───────────────────────────────────────────────────────────
        private void DrawDiagram()
        {
            EditorGUILayout.LabelField("CSNOH 通道分布", EditorStyles.boldLabel);
            var style = new GUIStyle(EditorStyles.helpBox) { richText = true, fontSize = 11 };
            EditorGUILayout.LabelField(
                "<b>CS</b>  ▸  RGB = Color   │  A = Smoothness\n" +
                "<b>NOH</b> ▸  RG  = Normal.xy  │  B = AO  │  A = Height\n" +
                "                （Normal Z 由消费方 reconstruct: Z=sqrt(1−x²−y²)）",
                style, GUILayout.ExpandWidth(true));
        }

        // ── Inputs ────────────────────────────────────────────────────────────
        private void DrawInputs()
        {
            EditorGUILayout.LabelField("输入贴图", EditorStyles.boldLabel);

            // Color
            DrawTexRow("Color (RGB → CS.rgb)", ref _texColor, null, null, null, 0f, false, false);

            // Smoothness
            DrawScalarRow("Smoothness → CS.a",
                ref _texSmooth, ref _chanSmooth, ref _invSmooth, ref _constSmooth);

            // Normal
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Normal Map → NOH.rg", EditorStyles.miniBoldLabel);
            _texNormal = (Texture2D)EditorGUILayout.ObjectField(
                "法线贴图", _texNormal, typeof(Texture2D), false);
            _normalEncoding = (NormalEncoding)EditorGUILayout.EnumPopup(
                "编码格式", _normalEncoding);
            EditorGUILayout.HelpBox(
                _normalEncoding == NormalEncoding.StandardRGB
                    ? "标准 RGB 法线图：XY 存在 RG 通道（值域 0-1）"
                    : "BC5/DXT5nm：X 存在 Alpha，Y 存在 G 通道",
                MessageType.None);
            EditorGUILayout.EndVertical();

            // AO
            DrawScalarRow("AO → NOH.b",
                ref _texAO, ref _chanAO, ref _invAO, ref _constAO);

            // Height
            DrawScalarRow("Height → NOH.a",
                ref _texHeight, ref _chanHeight, ref _invHeight, ref _constHeight);
        }

        private void DrawTexRow(string label, ref Texture2D tex,
            SourceChannel? chan, bool? inv, float? constVal,
            float constFallback, bool showChan, bool showInv)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            tex = (Texture2D)EditorGUILayout.ObjectField("贴图", tex, typeof(Texture2D), false);
            EditorGUILayout.EndVertical();
        }

        private void DrawScalarRow(string label,
            ref Texture2D tex, ref SourceChannel chan, ref bool inv, ref float constVal)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            tex = (Texture2D)EditorGUILayout.ObjectField("贴图", tex, typeof(Texture2D), false);

            using (new EditorGUI.DisabledScope(tex == null))
                chan = (SourceChannel)EditorGUILayout.EnumPopup("采样通道", chan);

            using (new EditorGUI.DisabledScope(tex != null))
                constVal = EditorGUILayout.Slider("常数值（无贴图时）", constVal, 0f, 1f);

            inv = EditorGUILayout.Toggle("反转", inv);
            EditorGUILayout.EndVertical();
        }

        // ── Output settings ───────────────────────────────────────────────────
        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("输出设置", EditorStyles.boldLabel);

            _resolution = (OutputResolution)EditorGUILayout.EnumPopup("分辨率", _resolution);
            _format     = (OutputFormat)EditorGUILayout.EnumPopup("格式", _format);

            EditorGUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("输出目录", _outputPath);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                string p = EditorUtility.OpenFolderPanel("选择输出目录", "Assets", "");
                if (!string.IsNullOrEmpty(p)) _outputPath = AbsToAsset(p);
            }
            if (GUILayout.Button("自动", GUILayout.Width(40)))
                _outputPath = AutoOutputPath();
            EditorGUILayout.EndHorizontal();

            _fileNameCS  = EditorGUILayout.TextField("CS 文件名",  _fileNameCS);
            _fileNameNOH = EditorGUILayout.TextField("NOH 文件名", _fileNameNOH);

            string ext = _format.ToString().ToLower();
            EditorGUILayout.LabelField("预览",
                $"{_outputPath}/{_fileNameCS}.{ext}  +  {_fileNameNOH}.{ext}",
                EditorStyles.miniLabel);
        }

        private string AutoOutputPath()
        {
            foreach (var t in new[] { _texColor, _texNormal, _texAO, _texHeight, _texSmooth })
            {
                if (t == null) continue;
                string p = AssetDatabase.GetAssetPath(t);
                if (!string.IsNullOrEmpty(p))
                    return Path.GetDirectoryName(p).Replace("\\", "/");
            }
            return "Assets";
        }

        // ── Generate button ───────────────────────────────────────────────────
        private void DrawGenerateButton()
        {
            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button("生成 CSNOH 贴图", GUILayout.Height(36)))
                    Execute();
            }
            if (_busy)
                EditorGUILayout.HelpBox("GPU 正在处理中…", MessageType.None);
        }

        // ═════════════════════════════════════════════════════════════════════
        // GPU Dispatch
        // ═════════════════════════════════════════════════════════════════════
        private void Execute()
        {
            if (_shader == null || _busy) return;

            int res = (int)_resolution;

            // Infer resolution from inputs if they exist
            int autoRes = BestRes(_texColor, _texNormal, _texAO, _texHeight, _texSmooth);
            if (autoRes > 0) res = autoRes;

            // Create output RenderTextures
            var rtCS  = CreateRT(res);
            var rtNOH = CreateRT(res);

            int kernel = _shader.FindKernel("CSMain");

            // Outputs
            _shader.SetTexture(kernel, "ResultCS",  rtCS);
            _shader.SetTexture(kernel, "ResultNOH", rtNOH);
            _shader.SetInt("Resolution", res);

            // Color
            BindTex(kernel, "TexColor", "HasColor", _texColor);

            // Smoothness (scalar)
            BindScalar(kernel, "TexSmooth", "HasSmooth", "ChanSmooth", "InvSmooth", "ConstSmooth",
                _texSmooth, _chanSmooth, _invSmooth, _constSmooth);

            // Normal
            BindTex(kernel, "TexNormal", "HasNormal", _texNormal);
            _shader.SetInt("NormalEncoding", (int)_normalEncoding);

            // AO (scalar)
            BindScalar(kernel, "TexAO", "HasAO", "ChanAO", "InvAO", "ConstAO",
                _texAO, _chanAO, _invAO, _constAO);

            // Height (scalar)
            BindScalar(kernel, "TexHeight", "HasHeight", "ChanHeight", "InvHeight", "ConstHeight",
                _texHeight, _chanHeight, _invHeight, _constHeight);

            int groups = Mathf.CeilToInt(res / 8f);
            _shader.Dispatch(kernel, groups, groups, 1);
            _busy = true;

            // Readback CS
            AsyncGPUReadback.Request(rtCS, 0, TextureFormat.RGBAFloat, reqCS =>
            {
                if (reqCS.hasError)
                {
                    Debug.LogError("[CSNOH] CS readback error.");
                    _busy = false;
                    rtCS.Release();
                    rtNOH.Release();
                    return;
                }

                // Readback NOH after CS is done
                AsyncGPUReadback.Request(rtNOH, 0, TextureFormat.RGBAFloat, reqNOH =>
                {
                    _busy = false;
                    rtCS.Release();
                    rtNOH.Release();

                    if (reqNOH.hasError)
                    {
                        Debug.LogError("[CSNOH] NOH readback error.");
                        return;
                    }

                    string outDir = string.IsNullOrEmpty(_outputPath) ? AutoOutputPath() : _outputPath;

                    WriteTexture(reqCS.GetData<float>(),  res, outDir, _fileNameCS);
                    WriteTexture(reqNOH.GetData<float>(), res, outDir, _fileNameNOH);

                    AssetDatabase.Refresh();

                    string ext = _format.ToString().ToLower();
                    PingAsset($"{outDir}/{_fileNameCS}.{ext}");
                    PingAsset($"{outDir}/{_fileNameNOH}.{ext}");

                    Debug.Log($"[CSNOH] 打包完成 → {outDir}/{_fileNameCS}.{ext} + {_fileNameNOH}.{ext}");
                    Repaint();
                });
            });
        }

        // ── Bind helpers ─────────────────────────────────────────────────────
        private void BindTex(int kernel, string texName, string hasName, Texture2D tex)
        {
            bool has = tex != null;
            _shader.SetTexture(kernel, texName, has ? (Texture)EnsureReadable(tex) : Texture2D.grayTexture);
            _shader.SetInt(hasName, has ? 1 : 0);
        }

        private void BindScalar(int kernel,
            string texName, string hasName, string chanName, string invName, string constName,
            Texture2D tex, SourceChannel chan, bool inv, float constVal)
        {
            bool has = tex != null;
            _shader.SetTexture(kernel, texName, has ? (Texture)EnsureReadable(tex) : Texture2D.grayTexture);
            _shader.SetInt(hasName,   has ? 1 : 0);
            _shader.SetInt(chanName,  (int)chan);
            _shader.SetInt(invName,   inv ? 1 : 0);
            _shader.SetFloat(constName, constVal);
        }

        // ── Utility ───────────────────────────────────────────────────────────
        private static RenderTexture CreateRT(int res)
        {
            var rt = new RenderTexture(res, res, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.Create();
            return rt;
        }

        private void WriteTexture(Unity.Collections.NativeArray<float> data, int res,
            string assetDir, string fileName)
        {
            string absDir = Application.dataPath.Replace("Assets", "") + assetDir;
            if (!Directory.Exists(absDir)) Directory.CreateDirectory(absDir);

            string ext  = _format == OutputFormat.PNG ? ".png" : ".tga";
            string assetPath = $"{assetDir}/{fileName}{ext}";
            string absPath   = Application.dataPath.Replace("Assets", "") + assetPath;

            var tex = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
            tex.SetPixelData(data, 0);
            tex.Apply();

            byte[] bytes = _format == OutputFormat.PNG ? tex.EncodeToPNG() : tex.EncodeToTGA();
            File.WriteAllBytes(absPath, bytes);
            DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void PingAsset(string assetPath)
        {
            var obj = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (obj != null) EditorGUIUtility.PingObject(obj);
        }

        private static int BestRes(params Texture2D[] textures)
        {
            int best = 0;
            foreach (var t in textures)
                if (t != null) best = Mathf.Max(best, Mathf.Max(t.width, t.height));
            return best;
        }

        private static Texture2D EnsureReadable(Texture2D src)
        {
            if (src == null) return null;
            var path = AssetDatabase.GetAssetPath(src);
            if (!string.IsNullOrEmpty(path))
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && imp.isReadable) return src;
            }
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

        private static string AbsToAsset(string abs)
        {
            string data = Application.dataPath.Replace("\\", "/");
            abs = abs.Replace("\\", "/");
            return abs.StartsWith(data) ? "Assets" + abs.Substring(data.Length) : abs;
        }
    }
}
