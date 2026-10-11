using System;
using UnityEditor;
using UnityEngine;

namespace Dyskotron.NineSlice
{
    /// <summary>
    /// The 9-Slice Optimizer dialog: shows the sprite with its 9-slice lines, lets you pick how many pixels the stretch zones keep
    /// (Center Width / Center Height, which size the top/bottom and left/right edges too), previews the result, and writes it as a
    /// new asset or over the source. Open it from a texture's or sprite's context menu (Assets › 9-Slice › Optimize 9-Slice…) or
    /// Tools › UI › 9-Slice Optimizer.
    /// </summary>
    public class NineSliceOptimizerWindow : EditorWindow
    {
        const string MenuPath = "Assets/9-Slice/Optimize 9-Slice...";
        const int MaxPreviewHeight = 260;

        [SerializeField] UnityEngine.Object asset;
        [SerializeField] int centerWidth = 4;
        [SerializeField] int centerHeight = 4;
        [SerializeField] Vector4 border;
        [SerializeField] bool editBorder;
        [SerializeField] int previewZoom = 1;

        NineSliceOptimizer.Source source;
        string sourceError;
        UnityEngine.Object loadedAsset;
        Texture2D previewTexture;
        int previewKey;
        int outWidth, outHeight;
        string resultError;

        [MenuItem(MenuPath, false, 30)]
        static void OpenFromAsset()
        {
            Open(Selection.activeObject);
        }

        [MenuItem(MenuPath, true)]
        static bool ValidateOpenFromAsset()
        {
            UnityEngine.Object o = Selection.activeObject;
            if (o is Sprite) return true;
            return o is Texture2D && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(o)) is TextureImporter;
        }

        [MenuItem("Tools/UI/9-Slice Optimizer")]
        static void OpenFromMenu()
        {
            Open(ValidateOpenFromAsset() ? Selection.activeObject : null);
        }

        public static NineSliceOptimizerWindow Open(UnityEngine.Object asset)
        {
            var window = GetWindow<NineSliceOptimizerWindow>(true, "9-Slice Optimizer");
            window.minSize = new Vector2(420, 520);
            if (asset != null) window.asset = asset;
            window.Reload();
            window.Show();
            return window;
        }

        void OnEnable() => Reload();

        void OnDisable() => ReleasePreview();

        void Reload()
        {
            source = null;
            sourceError = null;
            loadedAsset = asset;
            ReleasePreview();
            if (asset == null) return;
            source = NineSliceOptimizer.Load(asset, out sourceError);
            if (source != null) border = source.Border;
        }

        void ReleasePreview()
        {
            if (previewTexture != null) DestroyImmediate(previewTexture);
            previewTexture = null;
            previewKey = 0;
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            asset = EditorGUILayout.ObjectField("Sprite / Texture", asset, typeof(UnityEngine.Object), false);
            if (EditorGUI.EndChangeCheck() || asset != loadedAsset) Reload();

            if (asset == null)
            {
                EditorGUILayout.HelpBox("Pick a sprite texture whose 9-slice border is set in the Sprite Editor.", MessageType.Info);
                return;
            }
            if (source == null)
            {
                EditorGUILayout.HelpBox(sourceError ?? "Can't load the sprite.", MessageType.Error);
                return;
            }

            DrawSourceInfo();
            DrawParameters();
            if (!DrawResult()) return;
            DrawActions();
        }

        void DrawSourceInfo()
        {
            string mode = source.Multiple ? $"Multiple ({source.SpriteCount} sprite{(source.SpriteCount == 1 ? "" : "s")}, using \"{source.SpriteName}\")" : "Single";
            EditorGUILayout.LabelField("Source", $"{source.Rect.width} × {source.Rect.height} px, {mode}", EditorStyles.miniLabel);
            if (!source.PixelsFromFile)
                EditorGUILayout.HelpBox("Not a PNG/JPG: the pixels come from the imported texture (compression and Max Size apply).", MessageType.Warning);

            Rect area = GUILayoutUtility.GetRect(10, MaxPreviewHeight, GUILayout.ExpandWidth(true));
            DrawSpriteWithLines(area, source.Texture, source.Rect, border, "source");
        }

        void DrawParameters()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Stretch zones", EditorStyles.boldLabel);
            int midW = source.Rect.width - Mathf.RoundToInt(border.x) - Mathf.RoundToInt(border.z);
            int midH = source.Rect.height - Mathf.RoundToInt(border.y) - Mathf.RoundToInt(border.w);
            centerWidth = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Center Width", "Pixels the stretched column (centre, top and bottom edges) keeps"), centerWidth));
            centerHeight = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Center Height", "Pixels the stretched row (centre, left and right edges) keeps"), centerHeight));
            EditorGUILayout.LabelField(" ", $"Stretch zones in the source: {Mathf.Max(0, midW)} × {Mathf.Max(0, midH)} px (never grown)", EditorStyles.miniLabel);
            bool widthKept = NineSliceShrink.IsUnsliced(Mathf.RoundToInt(border.x), Mathf.RoundToInt(border.z));
            bool heightKept = NineSliceShrink.IsUnsliced(Mathf.RoundToInt(border.y), Mathf.RoundToInt(border.w));
            if (widthKept && heightKept)
                EditorGUILayout.LabelField(" ", "No border at all: nothing is shrunk.", EditorStyles.miniLabel);
            else if (widthKept)
                EditorGUILayout.LabelField(" ", "No left/right border: the width is kept, only the height shrinks.", EditorStyles.miniLabel);
            else if (heightKept)
                EditorGUILayout.LabelField(" ", "No top/bottom border: the height is kept, only the width shrinks.", EditorStyles.miniLabel);

            editBorder = EditorGUILayout.Foldout(editBorder, "Border (from the sprite; edit to override)", true);
            if (editBorder)
            {
                EditorGUI.indentLevel++;
                int l = EditorGUILayout.IntField("Left", Mathf.RoundToInt(border.x));
                int b = EditorGUILayout.IntField("Bottom", Mathf.RoundToInt(border.y));
                int r = EditorGUILayout.IntField("Right", Mathf.RoundToInt(border.z));
                int t = EditorGUILayout.IntField("Top", Mathf.RoundToInt(border.w));
                border = new Vector4(Mathf.Max(0, l), Mathf.Max(0, b), Mathf.Max(0, r), Mathf.Max(0, t));
                if (GUILayout.Button("Reset to the sprite's border", GUILayout.Width(200))) border = source.Border;
                EditorGUI.indentLevel--;
            }
            if (border == Vector4.zero)
                EditorGUILayout.HelpBox("The sprite has no 9-slice border: set it in the Sprite Editor (or in Border above). Without a border nothing is shrunk.", MessageType.Warning);
        }

        bool DrawResult()
        {
            int key = HashCode.Combine(border, centerWidth, centerHeight, source.AssetPath, source.SpriteName);
            if (key != previewKey)
            {
                ReleasePreview();
                previewKey = key;
                resultError = null;
                try
                {
                    Color32[] pixels = NineSliceOptimizer.Shrink(source, border, centerWidth, centerHeight, out outWidth, out outHeight);
                    previewTexture = new Texture2D(outWidth, outHeight, TextureFormat.RGBA32, false, false)
                    {
                        filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave
                    };
                    previewTexture.SetPixels32(pixels);
                    previewTexture.Apply(false, false);
                }
                catch (Exception e)
                {
                    resultError = e.Message;
                }
            }
            if (resultError != null)
            {
                EditorGUILayout.HelpBox(resultError, MessageType.Error);
                return false;
            }

            EditorGUILayout.Space(4);
            long before = (long)source.Rect.width * source.Rect.height, after = (long)outWidth * outHeight;
            EditorGUILayout.LabelField("Result", $"{outWidth} × {outHeight} px ({100.0 * after / Math.Max(1, before):0.#} % of the source's pixels)", EditorStyles.boldLabel);
            previewZoom = EditorGUILayout.IntSlider("Preview zoom", previewZoom, 1, 8);
            Rect area = GUILayoutUtility.GetRect(10, Mathf.Clamp(outHeight * previewZoom + 8, 40, MaxPreviewHeight), GUILayout.ExpandWidth(true));
            DrawSpriteWithLines(area, previewTexture, new RectInt(0, 0, outWidth, outHeight), border, "result", previewZoom);
            return true;
        }

        void DrawActions()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save As New…", GUILayout.Height(28)))
                {
                    string suggested = NineSliceOptimizer.DefaultNewPath(source);
                    string path = EditorUtility.SaveFilePanelInProject("Save optimized 9-slice sprite", System.IO.Path.GetFileName(suggested), "png",
                                                                       "The new sprite gets the same border and import settings.",
                                                                       System.IO.Path.GetDirectoryName(suggested));
                    if (!string.IsNullOrEmpty(path)) Run(() =>
                    {
                        Color32[] pixels = NineSliceOptimizer.Shrink(source, border, centerWidth, centerHeight, out int w, out int h);
                        Texture2D created = NineSliceOptimizer.SaveAsNew(source, pixels, w, h, border, path);
                        EditorGUIUtility.PingObject(created);
                        Debug.Log($"9-Slice Optimizer: wrote {path} ({w} × {h} px) from {source.AssetPath}.", created);
                    });
                }

                using (new EditorGUI.DisabledScope(!source.CanReplace))
                {
                    if (GUILayout.Button("Replace Source", GUILayout.Height(28)) &&
                        EditorUtility.DisplayDialog("Replace the source texture?",
                                                    $"{source.AssetPath} will be overwritten with the {outWidth} × {outHeight} px result and its sprite border set. " +
                                                    "References to the sprite keep working. This can't be undone (use version control).",
                                                    "Replace", "Cancel"))
                    {
                        Run(() =>
                        {
                            Color32[] pixels = NineSliceOptimizer.Shrink(source, border, centerWidth, centerHeight, out int w, out int h);
                            NineSliceOptimizer.ReplaceSource(source, pixels, w, h, border);
                            Debug.Log($"9-Slice Optimizer: replaced {source.AssetPath} with the {w} × {h} px result.", source.Texture);
                            Reload();
                        });
                    }
                }
            }
            if (!source.CanReplace)
                EditorGUILayout.LabelField(source.SpriteCount > 1 ? "Replace Source needs a sheet with one sprite (this one is cropped to the chosen sprite: save it as new)."
                                                                  : "Replace Source needs a PNG source.", EditorStyles.wordWrappedMiniLabel);
        }

        static void Run(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("9-Slice Optimizer", "Failed: " + e.Message, "OK");
            }
        }

        /// <summary>Draws <paramref name="rect"/> of <paramref name="texture"/> fitted into <paramref name="area"/> with the four 9-slice lines over it.</summary>
        static void DrawSpriteWithLines(Rect area, Texture2D texture, RectInt rect, Vector4 border, string label, int maxScale = 0)
        {
            if (texture == null || rect.width <= 0 || rect.height <= 0) return;
            EditorGUI.DrawRect(area, new Color(0.15f, 0.15f, 0.15f, 1));
            float scale = Mathf.Min((area.width - 8) / rect.width, (area.height - 8) / rect.height);
            if (maxScale > 0) scale = Mathf.Min(scale, maxScale);
            if (scale >= 1) scale = Mathf.Floor(scale); // whole pixels when magnified
            float w = rect.width * scale, h = rect.height * scale;
            var drawn = new Rect(area.x + (area.width - w) / 2, area.y + (area.height - h) / 2, w, h);

            // Texture coordinates of the sprite rect (GUI draws top-down, texture rows are bottom-up: the uv rect is in texture space).
            var uv = new Rect((float)rect.x / texture.width, (float)rect.y / texture.height, (float)rect.width / texture.width, (float)rect.height / texture.height);
            DrawCheckerboard(drawn);
            GUI.DrawTextureWithTexCoords(drawn, texture, uv, true);

            Color line = new Color(0.2f, 0.9f, 1f, 0.9f);
            float left = drawn.x + border.x * scale, right = drawn.xMax - border.z * scale;
            float top = drawn.y + border.w * scale, bottom = drawn.yMax - border.y * scale; // y grows down on screen: top border at the top
            if (border.x > 0) EditorGUI.DrawRect(new Rect(left - 0.5f, drawn.y, 1, drawn.height), line);
            if (border.z > 0) EditorGUI.DrawRect(new Rect(right - 0.5f, drawn.y, 1, drawn.height), line);
            if (border.w > 0) EditorGUI.DrawRect(new Rect(drawn.x, top - 0.5f, drawn.width, 1), line);
            if (border.y > 0) EditorGUI.DrawRect(new Rect(drawn.x, bottom - 0.5f, drawn.width, 1), line);

            GUI.Label(new Rect(area.x + 4, area.y + 2, 200, 16), $"{label}: {rect.width} × {rect.height}", EditorStyles.miniLabel);
        }

        static void DrawCheckerboard(Rect r)
        {
            const float cell = 8;
            Color a = new Color(0.35f, 0.35f, 0.35f), b = new Color(0.45f, 0.45f, 0.45f);
            int cols = Mathf.CeilToInt(r.width / cell), rows = Mathf.CeilToInt(r.height / cell);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var c = new Rect(r.x + x * cell, r.y + y * cell, Mathf.Min(cell, r.xMax - (r.x + x * cell)), Mathf.Min(cell, r.yMax - (r.y + y * cell)));
                    EditorGUI.DrawRect(c, ((x + y) & 1) == 0 ? a : b);
                }
        }
    }
}
