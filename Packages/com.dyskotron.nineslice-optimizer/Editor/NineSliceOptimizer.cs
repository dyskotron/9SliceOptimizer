using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Dyskotron.NineSlice
{
    /// <summary>
    /// The asset side of the 9-slice optimiser: reads a sprite's pixels, rect and border from its texture asset, runs
    /// <see cref="NineSliceShrink"/>, and writes the result as a PNG with a matching importer (the same border, Single sprite mode),
    /// either as a new asset next to the source or over the source itself.
    /// </summary>
    public static class NineSliceOptimizer
    {
        /// <summary>One sprite of a texture asset, as the optimiser sees it.</summary>
        public class Source
        {
            public Texture2D Texture;
            public string AssetPath;
            public TextureImporter Importer;
            public Color32[] Pixels;      // bottom-up, the whole texture, as stored in the file
            public int Width, Height;
            public RectInt Rect;          // the sprite's rect in the texture
            public Vector4 Border;        // x left, y bottom, z right, w top
            public string SpriteName;
            public bool Multiple;         // the importer is in Multiple sprite mode
            public int SpriteCount;       // sprites in the sheet (1 in Single mode)
            public SpriteAlignment Alignment;
            public Vector2 Pivot;
            public bool PixelsFromFile;   // false: read through the imported texture (compressed/resized by the import)

            /// <summary>The source can be replaced in place: a PNG with one sprite, so references to its sprite survive.</summary>
            public bool CanReplace => AssetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && SpriteCount == 1;
        }

        /// <summary>Loads <paramref name="asset"/> (a Texture2D or a Sprite) as a source, or returns null with a reason.</summary>
        public static Source Load(UnityEngine.Object asset, out string error)
        {
            error = null;
            Texture2D texture = asset as Texture2D;
            Sprite sprite = asset as Sprite;
            if (sprite != null) texture = sprite.texture;
            if (texture == null) { error = "Select a texture or a sprite."; return null; }

            string path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { error = "The texture isn't an imported texture asset."; return null; }
            if (importer.textureType != TextureImporterType.Sprite) { error = "The texture isn't imported as a Sprite (2D and UI)."; return null; }

            var source = new Source { Texture = texture, AssetPath = path, Importer = importer };
            if (!ReadPixels(texture, path, source, out error)) return null;

            if (importer.spriteImportMode == SpriteImportMode.Single)
            {
                source.Rect = new RectInt(0, 0, source.Width, source.Height);
                source.Border = importer.spriteBorder;
                source.SpriteName = texture.name;
                source.SpriteCount = 1;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                source.Alignment = (SpriteAlignment)settings.spriteAlignment;
                source.Pivot = settings.spritePivot;
            }
            else if (importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                SpriteRect[] rects = SpriteRects(importer);
                if (rects.Length == 0) { error = "The sprite sheet has no sprites."; return null; }
                SpriteRect chosen = sprite != null ? rects.FirstOrDefault(r => r.name == sprite.name) ?? rects[0] : rects[0];
                source.Multiple = true;
                source.SpriteCount = rects.Length;
                source.Rect = new RectInt(Mathf.RoundToInt(chosen.rect.x), Mathf.RoundToInt(chosen.rect.y),
                                          Mathf.RoundToInt(chosen.rect.width), Mathf.RoundToInt(chosen.rect.height));
                source.Border = chosen.border;
                source.SpriteName = chosen.name;
                source.Alignment = chosen.alignment;
                source.Pivot = chosen.pivot;
            }
            else
            {
                error = $"Sprite mode {importer.spriteImportMode} isn't supported (use Single or Multiple).";
                return null;
            }
            return source;
        }

        /// <summary>Runs the shrink for <paramref name="source"/> with an (optionally edited) border.</summary>
        public static Color32[] Shrink(Source source, Vector4 border, int centerWidth, int centerHeight, out int outWidth, out int outHeight)
        {
            NineSliceShrink.Plan(source.Rect.width, source.Rect.height, border, centerWidth, centerHeight,
                                 out NineSliceShrink.AxisPlan x, out NineSliceShrink.AxisPlan y);
            outWidth = x.OutputLength;
            outHeight = y.OutputLength;
            return NineSliceShrink.Shrink(source.Pixels, source.Width, source.Height, source.Rect, x, y);
        }

        /// <summary>
        /// Writes the result as a new PNG asset at <paramref name="newAssetPath"/> (a Single sprite with <paramref name="border"/>,
        /// the source's other import settings copied) and returns the imported texture.
        /// </summary>
        public static Texture2D SaveAsNew(Source source, Color32[] pixels, int width, int height, Vector4 border, string newAssetPath)
        {
            WritePng(pixels, width, height, newAssetPath);
            AssetDatabase.ImportAsset(newAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(newAssetPath);

            var settings = new TextureImporterSettings();
            source.Importer.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteBorder = border;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)source.Alignment;
            settings.spritePivot = source.Pivot;
            importer.SetTextureSettings(settings);
            CopyPlatformSettings(source.Importer, importer);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(newAssetPath);
        }

        /// <summary>
        /// Overwrites the source PNG with the result and keeps its sprite: in Single mode the border is set on the importer, in
        /// Multiple mode (one sprite) the sprite's rect becomes the new size and its border is set, its name and ID kept, so every
        /// reference to the sprite keeps working. Requires <see cref="Source.CanReplace"/>.
        /// </summary>
        public static void ReplaceSource(Source source, Color32[] pixels, int width, int height, Vector4 border)
        {
            if (!source.CanReplace) throw new InvalidOperationException("The source can't be replaced in place (not a PNG, or several sprites).");
            WritePng(pixels, width, height, source.AssetPath);
            AssetDatabase.ImportAsset(source.AssetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(source.AssetPath);

            if (importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                ISpriteEditorDataProvider provider = DataProvider(importer);
                SpriteRect[] rects = provider.GetSpriteRects();
                rects[0].rect = new Rect(0, 0, width, height);
                rects[0].border = border;
                provider.SetSpriteRects(rects);
                provider.Apply();
            }
            else
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteBorder = border;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
            }
            importer.SaveAndReimport();
        }

        /// <summary>A default output path next to the source: "{name}_9s.png", made unique.</summary>
        public static string DefaultNewPath(Source source)
        {
            string dir = Path.GetDirectoryName(source.AssetPath)?.Replace('\\', '/');
            string name = source.Multiple && source.SpriteCount > 1 ? source.SpriteName : Path.GetFileNameWithoutExtension(source.AssetPath);
            return AssetDatabase.GenerateUniqueAssetPath($"{dir}/{name}_9s.png");
        }

        static void WritePng(Color32[] pixels, int width, int height, string assetPath)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                byte[] png = ImageConversion.EncodeToPNG(tex);
                string full = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, png);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Reads the texture's pixels as stored in the file (PNG/JPG through <see cref="ImageConversion.LoadImage"/>, so the
        /// import's compression, resize and read/write flag don't matter); other formats fall back to a blit of the imported texture.
        /// </summary>
        static bool ReadPixels(Texture2D texture, string path, Source source, out string error)
        {
            error = null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                try
                {
                    if (ImageConversion.LoadImage(tex, File.ReadAllBytes(Path.GetFullPath(path)), false))
                    {
                        source.Width = tex.width;
                        source.Height = tex.height;
                        source.Pixels = tex.GetPixels32();
                        source.PixelsFromFile = true;
                        return true;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            // Fallback: whatever the import produced (compressed, maybe resized), read through a render texture.
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                copy.Apply(false, false);
                source.Width = copy.width;
                source.Height = copy.height;
                source.Pixels = copy.GetPixels32();
                source.PixelsFromFile = false;
                return true;
            }
            catch (Exception e)
            {
                error = "Couldn't read the texture's pixels: " + e.Message;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        static SpriteRect[] SpriteRects(TextureImporter importer) => DataProvider(importer).GetSpriteRects();

        static ISpriteEditorDataProvider DataProvider(TextureImporter importer)
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            return provider;
        }

        static readonly string[] Platforms = { "Standalone", "Android", "iPhone", "WebGL", "Windows Store Apps", "tvOS", "Switch", "PS4", "PS5", "XboxOne" };

        static void CopyPlatformSettings(TextureImporter from, TextureImporter to)
        {
            to.SetPlatformTextureSettings(from.GetDefaultPlatformTextureSettings());
            foreach (string platform in Platforms)
            {
                TextureImporterPlatformSettings s = from.GetPlatformTextureSettings(platform);
                if (s.overridden) to.SetPlatformTextureSettings(s);
            }
        }
    }
}
