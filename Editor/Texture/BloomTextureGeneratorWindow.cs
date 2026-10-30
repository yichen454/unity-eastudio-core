using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    public sealed class BloomTextureGeneratorWindow : EditorWindow
    {
        private enum Background { Checkerboard, Black, White, Custom }
        private enum Channel { Composite, RGB, Alpha }

        [SerializeField] private Texture2D _source;
        [SerializeField] private BloomTextureSettings _settings = new BloomTextureSettings();
        [SerializeField] private Background _background;
        [SerializeField] private Channel _channel;
        [SerializeField] private Color _backgroundColor = Color.gray;
        [SerializeField] private bool _livePreview = true;
        [SerializeField] private float _previewHeight = 260;
        private BloomTextureResult _result;
        private Texture2D _preview;
        private Vector2 _scroll;
        private bool _dirty = true;
        private string _error;
        private Hash128 _sourceHash;
        private double _refreshAt = double.PositiveInfinity;

        private void OnEnable()
        {
            EditorApplication.update += UpdatePreview;
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _error = null;
            _refreshAt = EditorApplication.timeSinceStartup + 0.2;
            Repaint();
        }

        private void UpdatePreview()
        {
            if (!_livePreview || !_dirty || _source == null ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < _refreshAt) return;
            // One attempt per change, including invalid settings and GPU failures.
            _refreshAt = double.PositiveInfinity;
            Generate(false);
            Repaint();
        }

        [MenuItem("Tools/EAStudio/贴图/辉光贴图生成")]
        public static void Open()
        {
            var window = GetWindow<BloomTextureGeneratorWindow>("Bloom Texture Generator");
            window.minSize = new Vector2(420, 650);
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            _result?.Dispose();
            _result = null;
            ClearPreview();
        }

        private void OnProjectChange()
        {
            if (_source != null && _result != null && _source.imageContentsHash != _sourceHash)
                MarkDirty();
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Bloom Texture Generator", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _source = (Texture2D)EditorGUILayout.ObjectField("Source Texture", _source, typeof(Texture2D), false);
            if (_source != null)
            {
                EditorGUILayout.LabelField("Imported Source Size", $"{_source.width} x {_source.height}");
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(_source)) as TextureImporter;
                if (importer != null)
                    EditorGUILayout.LabelField("Source Import", $"{_source.graphicsFormat}; Max {importer.maxTextureSize}; sRGB {importer.sRGBTexture}");
            }
            _settings.Threshold = EditorGUILayout.FloatField("Threshold (Linear)", _settings.Threshold);
            _settings.SoftKnee = EditorGUILayout.FloatField("Soft Knee", _settings.SoftKnee);
            _settings.Intensity = EditorGUILayout.Slider("Intensity", _settings.Intensity, 0, 10);
            _settings.Radius = EditorGUILayout.IntSlider("Radius (Pixels)", _settings.Radius, 0, 256);
            _settings.Tint = EditorGUILayout.ColorField(new GUIContent("Tint"), _settings.Tint, true, false, false);
            _settings.ExtraPadding = EditorGUILayout.IntField("Extra Padding", _settings.ExtraPadding);
            _settings.Output = (BloomTextureOutput)EditorGUILayout.EnumPopup("Output Mode", _settings.Output);
            _settings.Exposure = EditorGUILayout.Slider("Export Exposure (EV)", _settings.Exposure, -16, 16);
            if (EditorGUI.EndChangeCheck()) MarkDirty();
            EditorGUI.BeginChangeCheck();
            _livePreview = EditorGUILayout.Toggle("Live Preview", _livePreview);
            if (EditorGUI.EndChangeCheck() && _livePreview && _dirty) MarkDirty();
            EditorGUILayout.HelpBox("Transparency is detected from actual alpha pixels. Transparent sources expand automatically; opaque composites keep their original size. Extra Padding adds transparent canvas without scaling the source.", MessageType.Info);
            using (new EditorGUI.DisabledScope(_source == null))
                if (GUILayout.Button("Refresh Preview", GUILayout.Height(26))) Generate();
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (_result != null)
            {
                if (_dirty) EditorGUILayout.HelpBox(_livePreview && !double.IsPositiveInfinity(_refreshAt)
                    ? "Updating preview..." : "Settings or source changed. Refresh before exporting.", MessageType.Info);
                EditorGUILayout.LabelField("Detected Source", _result.HasTransparency ? "Contains transparency" : "Fully opaque");
                EditorGUILayout.LabelField("Output Dimensions", $"{_result.Texture.width} x {_result.Texture.height}");
                EditorGUILayout.LabelField("Padding Per Side", _result.Padding.ToString());
                var uv = _result.SourceRect;
                EditorGUILayout.LabelField("Source UV Rect", $"({uv.x:F4}, {uv.y:F4}, {uv.width:F4}, {uv.height:F4})");
                if (_result.ClippedFraction > 0)
                    EditorGUILayout.HelpBox($"LDR clipping: {_result.ClippedFraction:P3} of pixels. Reduce intensity or exposure to retain brightness detail.", MessageType.Warning);
                if (_result.HaloTouchesEdge)
                    EditorGUILayout.HelpBox("Visible bloom reaches the output edge. Add Extra Padding to avoid clipping the halo.", MessageType.Warning);
                EditorGUI.BeginChangeCheck();
                _channel = (Channel)EditorGUILayout.EnumPopup("Preview Channel", _channel);
                _background = (Background)EditorGUILayout.EnumPopup("Preview Background", _background);
                if (_background == Background.Custom)
                    _backgroundColor = EditorGUILayout.ColorField("Background Color", _backgroundColor);
                if (EditorGUI.EndChangeCheck()) BuildPreview();
                _previewHeight = EditorGUILayout.Slider("Preview Height", _previewHeight, 120, 480);
                if (_preview != null)
                {
                    Rect rect = GUILayoutUtility.GetRect(1, _previewHeight, GUILayout.ExpandWidth(true), GUILayout.Height(_previewHeight));
                    EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
                    GUI.DrawTexture(rect, _preview, ScaleMode.ScaleToFit, false);
                }
                EditorGUILayout.LabelField("Preview fits the bounded area. Export uses full resolution.", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("Straight alpha: use SrcAlpha / OneMinusSrcAlpha, or SrcAlpha / One for additive VFX. Do not multiply RGB by alpha twice. Padding changes the source UV footprint.", MessageType.Info);
                using (new EditorGUI.DisabledScope(_dirty))
                    if (GUILayout.Button("Export Transparent PNG", GUILayout.Height(30))) Export();
            }
            EditorGUILayout.EndScrollView();
        }

        private void Generate(bool showProgress = true)
        {
            _refreshAt = double.PositiveInfinity;
            try
            {
                var generated = BloomTextureGenerator.Generate(_source, _settings, showProgress);
                _result?.Dispose();
                _result = generated;
                _sourceHash = _source.imageContentsHash;
                _dirty = false;
                _error = null;
                BuildPreview();
            }
            catch (OperationCanceledException) { _error = "Generation cancelled. Previous preview retained."; }
            catch (Exception exception) { _error = exception.Message; }
        }

        private void BuildPreview()
        {
            ClearPreview();
            var texture = _result.Texture;
            var sourcePixels = texture.GetPixels32();
            float scale = Mathf.Min(1, 512f / Mathf.Max(texture.width, texture.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int sourceX = Mathf.Min(texture.width - 1, (int)((x + 0.5f) * texture.width / width));
                int sourceY = Mathf.Min(texture.height - 1, (int)((y + 0.5f) * texture.height / height));
                Color c = sourcePixels[sourceY * texture.width + sourceX];
                if (_channel == Channel.Alpha) c = new Color(c.a, c.a, c.a, 1);
                else if (_channel == Channel.RGB) c.a = 1;
                else
                {
                    Color background = _background == Background.Black ? Color.black :
                        _background == Background.White ? Color.white :
                        _background == Background.Custom ? _backgroundColor :
                        new Color(((x / 16 + y / 16) % 2 == 0) ? 0.35f : 0.55f,
                            ((x / 16 + y / 16) % 2 == 0) ? 0.35f : 0.55f,
                            ((x / 16 + y / 16) % 2 == 0) ? 0.35f : 0.55f);
                    c = (c.linear * c.a + background.linear * (1 - c.a)).gamma;
                    c.a = 1;
                }
                pixels[index] = c;
            }
            _preview = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            _preview.SetPixels32(pixels);
            _preview.Apply(false, true);
        }

        private void Export()
        {
            string path = EditorUtility.SaveFilePanelInProject("Export Bloom PNG", _source.name + "_Bloom", "png", "Choose a PNG destination.");
            if (string.IsNullOrEmpty(path)) return;
            bool overwrite = File.Exists(path) || File.Exists(path + ".meta");
            if (overwrite && !EditorUtility.DisplayDialog("Replace Bloom Output", "Replace the existing PNG and its texture import settings?", "Replace", "Cancel")) return;
            try
            {
                BloomTextureGenerator.Export(_result, _source, path, overwrite);
                _error = null;
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                ShowNotification(new GUIContent("Bloom PNG exported"));
            }
            catch (Exception exception) { _error = exception.Message; }
        }

        private void ClearPreview()
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = null;
        }
    }
}
