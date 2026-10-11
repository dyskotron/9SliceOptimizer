using System;
using UnityEngine;

namespace Dyskotron.NineSlice
{
    /// <summary>
    /// The pure maths of "optimising" a 9-slice sprite: the reverse of what a 9-slice renderer does. The renderer stretches the
    /// middle column (between the left and right borders) and the middle row (between the bottom and top borders); this shrinks
    /// those stretch zones to a few pixels, keeping the four corners pixel for pixel. The borders stay the same pixel values, so
    /// the result renders exactly like the source wherever the stretch zones are uniform along the stretch axis.
    /// Pixel rows are bottom-up (Unity's <c>GetPixels32</c> order); the border is Unity's <c>Vector4</c> order: x left, y bottom,
    /// z right, w top.
    /// </summary>
    public static class NineSliceShrink
    {
        /// <summary>What the operation will do for one axis: the two fixed ends and the stretch zone's source and output sizes.</summary>
        public readonly struct AxisPlan
        {
            public readonly int Low;        // pixels kept before the stretch zone (left or bottom border)
            public readonly int High;       // pixels kept after it (right or top border)
            public readonly int MidSource;  // the stretch zone's size in the source
            public readonly int MidOutput;  // its size in the output (<= MidSource, >= 1 when MidSource >= 1)

            public AxisPlan(int low, int high, int midSource, int midOutput)
            {
                Low = low;
                High = high;
                MidSource = midSource;
                MidOutput = midOutput;
            }

            public int SourceLength => Low + MidSource + High;
            public int OutputLength => Low + MidOutput + High;
        }

        /// <summary>
        /// Plans one axis. <paramref name="requestedMid"/> is clamped to [1, MidSource] (the zone never grows); a zone of size 0
        /// (the borders meet) stays 0. An axis with no border on either side isn't sliced at all and is kept as it is: a
        /// left/right-only (3-slice) button keeps its full height, a top/bottom-only one its full width. Throws when the borders
        /// overlap or are negative.
        /// </summary>
        public static AxisPlan PlanAxis(int sourceLength, int low, int high, int requestedMid)
        {
            if (low < 0 || high < 0) throw new ArgumentException("Negative border.");
            int midSource = sourceLength - low - high;
            if (midSource < 0) throw new ArgumentException($"The borders ({low} + {high}) exceed the size ({sourceLength}).");
            int midOutput = IsUnsliced(low, high) ? midSource : midSource == 0 ? 0 : Mathf.Clamp(requestedMid, 1, midSource);
            return new AxisPlan(low, high, midSource, midOutput);
        }

        /// <summary>An axis with no border on either side is not sliced: nothing on it is shrunk.</summary>
        public static bool IsUnsliced(int low, int high) => low == 0 && high == 0;

        /// <summary>Plans both axes for a sprite rect of <paramref name="width"/> × <paramref name="height"/> with <paramref name="border"/>.</summary>
        public static void Plan(int width, int height, Vector4 border, int centerWidth, int centerHeight, out AxisPlan x, out AxisPlan y)
        {
            x = PlanAxis(width, Mathf.RoundToInt(border.x), Mathf.RoundToInt(border.z), centerWidth);
            y = PlanAxis(height, Mathf.RoundToInt(border.y), Mathf.RoundToInt(border.w), centerHeight);
        }

        /// <summary>
        /// Shrinks the sprite rect <paramref name="rect"/> of a <paramref name="sourceWidth"/>-wide, bottom-up pixel array.
        /// Each output pixel is the box-filtered average of the source span it covers (corners and the fixed border pixels cover
        /// exactly one source pixel, so they are copied); colour is alpha-weighted so transparent pixels don't darken edges.
        /// </summary>
        public static Color32[] Shrink(Color32[] source, int sourceWidth, int sourceHeight, RectInt rect, AxisPlan x, AxisPlan y)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Length != sourceWidth * sourceHeight) throw new ArgumentException("Pixel count doesn't match the size.");
            if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > sourceWidth || rect.yMax > sourceHeight)
                throw new ArgumentException("The sprite rect lies outside the texture.");
            if (x.SourceLength != rect.width || y.SourceLength != rect.height)
                throw new ArgumentException("The plan doesn't match the sprite rect.");

            Span[] spansX = Spans(x);
            Span[] spansY = Spans(y);
            int outW = x.OutputLength, outH = y.OutputLength;
            var result = new Color32[outW * outH];

            for (int j = 0; j < outH; j++)
            {
                Span sy = spansY[j];
                for (int i = 0; i < outW; i++)
                {
                    Span sx = spansX[i];
                    result[j * outW + i] = Average(source, sourceWidth, rect, sx, sy);
                }
            }
            return result;
        }

        /// <summary>A half-open source span [Start, End) in sprite-rect pixels an output pixel covers.</summary>
        public readonly struct Span
        {
            public readonly double Start, End;
            public Span(double start, double end) { Start = start; End = end; }
        }

        /// <summary>The source span of every output pixel along one axis: 1-pixel spans at the ends, MidSource / MidOutput wide in the zone.</summary>
        public static Span[] Spans(AxisPlan plan)
        {
            var spans = new Span[plan.OutputLength];
            for (int i = 0; i < plan.Low; i++) spans[i] = new Span(i, i + 1);
            double step = plan.MidOutput > 0 ? (double)plan.MidSource / plan.MidOutput : 0;
            for (int k = 0; k < plan.MidOutput; k++)
                spans[plan.Low + k] = new Span(plan.Low + k * step, plan.Low + (k + 1) * step);
            int firstHigh = plan.Low + plan.MidOutput;
            for (int k = 0; k < plan.High; k++)
            {
                int s = plan.Low + plan.MidSource + k;
                spans[firstHigh + k] = new Span(s, s + 1);
            }
            return spans;
        }

        static Color32 Average(Color32[] source, int sourceWidth, RectInt rect, Span sx, Span sy)
        {
            int x0 = (int)Math.Floor(sx.Start), x1 = (int)Math.Ceiling(sx.End);
            int y0 = (int)Math.Floor(sy.Start), y1 = (int)Math.Ceiling(sy.End);
            double r = 0, g = 0, b = 0, a = 0, wTotal = 0;
            for (int yy = y0; yy < y1; yy++)
            {
                double wy = Math.Min(yy + 1, sy.End) - Math.Max(yy, sy.Start);
                if (wy <= 0) continue;
                int row = (rect.yMin + yy) * sourceWidth + rect.xMin;
                for (int xx = x0; xx < x1; xx++)
                {
                    double wx = Math.Min(xx + 1, sx.End) - Math.Max(xx, sx.Start);
                    if (wx <= 0) continue;
                    double w = wx * wy;
                    Color32 c = source[row + xx];
                    double wa = w * c.a;
                    r += c.r * wa;
                    g += c.g * wa;
                    b += c.b * wa;
                    a += wa;
                    wTotal += w;
                }
            }
            if (wTotal <= 0) return new Color32(0, 0, 0, 0);
            if (a <= 0) return new Color32(0, 0, 0, 0);
            return new Color32(Round(r / a), Round(g / a), Round(b / a), Round(a / wTotal));
        }

        static byte Round(double v) => (byte)Mathf.Clamp(Mathf.RoundToInt((float)v), 0, 255);
    }
}
