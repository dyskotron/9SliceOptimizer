using System;
using NUnit.Framework;
using UnityEngine;

namespace Dyskotron.NineSlice.Tests
{
    public class NineSliceShrinkTests
    {
        // A 10 × 8 "sprite" with border left 2, bottom 1, right 3, top 2: corners, edges and the centre each a distinct colour,
        // the centre band varying along X so averaging shows.
        const int W = 10, H = 8;
        static readonly Vector4 Border = new Vector4(2, 1, 3, 2);

        static Color32[] Source()
        {
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int zx = x < 2 ? 0 : x < W - 3 ? 1 : 2;
                    int zy = y < 1 ? 0 : y < H - 2 ? 1 : 2;
                    byte r = (byte)(zx * 100 + zy * 10);
                    byte g = (byte)(zx == 1 ? (x - 2) * 40 : 7); // the horizontal stretch zone: 0, 40, 80, 120, 160 along X
                    px[y * W + x] = new Color32(r, g, (byte)(x + y * 16), 255);
                }
            return px;
        }

        [Test]
        public void Plan_ClampsTheZoneToItsSourceSize_AndNeverBelowOne()
        {
            var plan = NineSliceShrink.PlanAxis(W, 2, 3, 100);
            Assert.AreEqual(5, plan.MidSource);
            Assert.AreEqual(5, plan.MidOutput);
            Assert.AreEqual(W, plan.OutputLength);

            plan = NineSliceShrink.PlanAxis(W, 2, 3, 0);
            Assert.AreEqual(1, plan.MidOutput);
            Assert.AreEqual(6, plan.OutputLength);
        }

        [Test]
        public void Plan_BordersMeeting_LeaveNoZone()
        {
            var plan = NineSliceShrink.PlanAxis(W, 4, 6, 3);
            Assert.AreEqual(0, plan.MidSource);
            Assert.AreEqual(0, plan.MidOutput);
            Assert.AreEqual(W, plan.OutputLength);
        }

        [Test]
        public void Plan_NoBorderOnAnAxis_KeepsThatAxisWhole()
        {
            // A left/right-only (3-slice) button: no top or bottom border, so its height is kept, whatever Center Height asks.
            var plan = NineSliceShrink.PlanAxis(71, 0, 0, 4);
            Assert.AreEqual(71, plan.MidSource);
            Assert.AreEqual(71, plan.MidOutput);
            Assert.AreEqual(71, plan.OutputLength);
            Assert.IsTrue(NineSliceShrink.IsUnsliced(0, 0));
            Assert.IsFalse(NineSliceShrink.IsUnsliced(0, 5));

            // One border only (left = 63, right = 0): the zone from the border to the far edge still shrinks.
            plan = NineSliceShrink.PlanAxis(497, 63, 0, 4);
            Assert.AreEqual(63 + 4, plan.OutputLength);
        }

        [Test]
        public void Shrink_ThreeSliceButton_KeepsItsHeight()
        {
            // 497 × 71 with border left 63, right 61, no top or bottom (the game's button body).
            const int bw = 497, bh = 71;
            var src = new Color32[bw * bh];
            for (int py = 0; py < bh; py++)
                for (int px = 0; px < bw; px++)
                    src[py * bw + px] = new Color32((byte)(px % 256), (byte)(py * 3), 0, 255);
            NineSliceShrink.Plan(bw, bh, new Vector4(63, 0, 61, 0), 4, 4, out var x, out var y);
            Color32[] result = NineSliceShrink.Shrink(src, bw, bh, new RectInt(0, 0, bw, bh), x, y);
            Assert.AreEqual(63 + 4 + 61, x.OutputLength);
            Assert.AreEqual(bh, y.OutputLength);
            // Every row is copied unchanged outside the zone: the vertical gradient survives.
            for (int yy = 0; yy < bh; yy++)
            {
                Assert.AreEqual(src[yy * bw + 10].g, result[yy * x.OutputLength + 10].g);
                Assert.AreEqual(src[yy * bw + (bw - 10)].g, result[yy * x.OutputLength + (x.OutputLength - 10)].g);
            }
        }

        [Test]
        public void Plan_OverlappingBorders_Throw()
        {
            Assert.Throws<ArgumentException>(() => NineSliceShrink.PlanAxis(W, 6, 6, 1));
            Assert.Throws<ArgumentException>(() => NineSliceShrink.PlanAxis(W, -1, 0, 1));
        }

        [Test]
        public void Shrink_OutputSize_IsBordersPlusCenter()
        {
            NineSliceShrink.Plan(W, H, Border, 1, 2, out var x, out var y);
            Color32[] result = NineSliceShrink.Shrink(Source(), W, H, new RectInt(0, 0, W, H), x, y);
            Assert.AreEqual(2 + 1 + 3, x.OutputLength);
            Assert.AreEqual(1 + 2 + 2, y.OutputLength);
            Assert.AreEqual(x.OutputLength * y.OutputLength, result.Length);
        }

        [Test]
        public void Shrink_CopiesTheCorners_PixelForPixel()
        {
            Color32[] src = Source();
            NineSliceShrink.Plan(W, H, Border, 1, 1, out var x, out var y);
            Color32[] result = NineSliceShrink.Shrink(src, W, H, new RectInt(0, 0, W, H), x, y);
            int ow = x.OutputLength, oh = y.OutputLength;

            // bottom-left 2 × 1, bottom-right 3 × 1, top-left 2 × 2, top-right 3 × 2
            for (int yy = 0; yy < 1; yy++) for (int xx = 0; xx < 2; xx++) AssertPixel(src[yy * W + xx], result[yy * ow + xx]);
            for (int yy = 0; yy < 1; yy++) for (int xx = 0; xx < 3; xx++) AssertPixel(src[yy * W + (W - 3 + xx)], result[yy * ow + (ow - 3 + xx)]);
            for (int yy = 0; yy < 2; yy++) for (int xx = 0; xx < 2; xx++) AssertPixel(src[(H - 2 + yy) * W + xx], result[(oh - 2 + yy) * ow + xx]);
            for (int yy = 0; yy < 2; yy++) for (int xx = 0; xx < 3; xx++) AssertPixel(src[(H - 2 + yy) * W + (W - 3 + xx)], result[(oh - 2 + yy) * ow + (ow - 3 + xx)]);
        }

        [Test]
        public void Shrink_AveragesTheStretchZone_AlongTheStretchAxisOnly()
        {
            Color32[] src = Source();
            NineSliceShrink.Plan(W, H, Border, 1, 1, out var x, out var y);
            Color32[] result = NineSliceShrink.Shrink(src, W, H, new RectInt(0, 0, W, H), x, y);
            int ow = x.OutputLength;

            // The bottom edge's single centre pixel: the mean of g = 0, 40, 80, 120, 160 along X at row 0 = 80.
            Color32 bottomEdge = result[0 * ow + 2];
            Assert.AreEqual(80, bottomEdge.g);
            Assert.AreEqual(100, bottomEdge.r); // zone colour unchanged: uniform along the zone
            Assert.AreEqual(255, bottomEdge.a);

            // The left edge's centre pixel (column 0, the middle row): rows 1..5 of column 0, b = x + y * 16 averaged over y = 1..5 → 48.
            Color32 leftEdge = result[1 * ow + 0];
            Assert.AreEqual(48, leftEdge.b);
            Assert.AreEqual(10, leftEdge.r);
        }

        [Test]
        public void Shrink_WithTwoCenterPixels_SplitsTheZoneInHalves()
        {
            Color32[] src = Source();
            NineSliceShrink.Plan(W, H, new Vector4(2, 1, 2, 2), 2, 1, out var x, out var y); // a 6-wide zone: 3 source pixels per output pixel
            Color32[] result = NineSliceShrink.Shrink(src, W, H, new RectInt(0, 0, W, H), x, y);
            int ow = x.OutputLength;
            Assert.AreEqual(6, ow);
            // columns 2, 3, 4 → g = 0, 40, 80 → 40; columns 5, 6, 7 → 120, 160 and column 7 (zx = 2 → g = 7) → (120 + 160 + 7) / 3 ≈ 96
            Assert.AreEqual(40, result[0 * ow + 2].g);
            Assert.AreEqual(96, result[0 * ow + 3].g);
        }

        [Test]
        public void Shrink_TransparentPixels_DontDarkenTheColour()
        {
            // A 5 × 1 strip with a 1-px left border: the 4-px zone (two opaque, two transparent pixels) shrinks to one pixel.
            var src = new Color32[5 * 1];
            src[0] = new Color32(9, 9, 9, 255);
            src[1] = new Color32(200, 100, 50, 255);
            src[2] = new Color32(200, 100, 50, 255);
            src[3] = new Color32(0, 0, 0, 0);
            src[4] = new Color32(0, 0, 0, 0);
            var x = NineSliceShrink.PlanAxis(5, 1, 0, 1);
            var y = NineSliceShrink.PlanAxis(1, 0, 0, 1);
            Color32[] result = NineSliceShrink.Shrink(src, 5, 1, new RectInt(0, 0, 5, 1), x, y);
            Assert.AreEqual(2, result.Length);
            AssertPixel(src[0], result[0]);
            AssertPixel(new Color32(200, 100, 50, 128), result[1]);
        }

        [Test]
        public void Shrink_UsesTheSpriteRect_InsideABiggerTexture()
        {
            // The 10 × 8 sprite placed at (5, 3) in a 20 × 16 sheet.
            Color32[] sprite = Source();
            var sheet = new Color32[20 * 16];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) sheet[(y + 3) * 20 + (x + 5)] = sprite[y * W + x];

            NineSliceShrink.Plan(W, H, Border, 1, 1, out var px, out var py);
            Color32[] fromSprite = NineSliceShrink.Shrink(sprite, W, H, new RectInt(0, 0, W, H), px, py);
            Color32[] fromSheet = NineSliceShrink.Shrink(sheet, 20, 16, new RectInt(5, 3, W, H), px, py);
            Assert.AreEqual(fromSprite.Length, fromSheet.Length);
            for (int i = 0; i < fromSprite.Length; i++) AssertPixel(fromSprite[i], fromSheet[i]);
        }

        [Test]
        public void Shrink_FullSizeZones_ReproduceTheSource()
        {
            Color32[] src = Source();
            NineSliceShrink.Plan(W, H, Border, 1000, 1000, out var x, out var y);
            Color32[] result = NineSliceShrink.Shrink(src, W, H, new RectInt(0, 0, W, H), x, y);
            Assert.AreEqual(src.Length, result.Length);
            for (int i = 0; i < src.Length; i++) AssertPixel(src[i], result[i]);
        }

        static void AssertPixel(Color32 expected, Color32 actual)
        {
            Assert.AreEqual(expected.r, actual.r, "r");
            Assert.AreEqual(expected.g, actual.g, "g");
            Assert.AreEqual(expected.b, actual.b, "b");
            Assert.AreEqual(expected.a, actual.a, "a");
        }
    }
}
