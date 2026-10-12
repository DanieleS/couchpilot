using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Playnite.SDK;

namespace CouchPilot.Library
{
    // The colours of a game's cover, for a light to take on while the game runs: the cover cut in
    // horizontal bands, from the bottom up, one vivid colour per band, so a floor lamp's segments
    // read like the cover standing upright. Vivid, not average: the average of a cover is nearly
    // always brown or grey. Every colour comes at full brightness, the light sets its own.
    public static class CoverPalette
    {
        public const int Bands = 7;
        private const int SampleWidth = 32;
        private const int SampleHeight = 48;
        private const int HueBins = 24;
        // Below these a pixel is grey, black or near it, and says nothing about the cover's colour.
        private const float MinSaturation = 0.3f;
        private const float MinValue = 0.25f;
        // A light washes out pale colours into white: lift them to at least this saturation.
        private const float LightSaturation = 0.55f;

        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly ConcurrentDictionary<string, List<int[]>> cache = new ConcurrentDictionary<string, List<int[]>>();

        // [r, g, b] per band, bottom band first; null when the cover is missing, not downloaded
        // or has no colour to speak of (black and white, say).
        public static List<int[]> Of(string coverPath)
        {
            if (string.IsNullOrEmpty(coverPath) || coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                if (!File.Exists(coverPath)) return null;
                var key = coverPath + "|" + File.GetLastWriteTimeUtc(coverPath).Ticks;
                return cache.GetOrAdd(key, _ => Compute(coverPath));
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Could not read the colours of {coverPath}");
                return null;
            }
        }

        private static List<int[]> Compute(string path)
        {
            var pixels = Sample(path);
            var all = Vivid(pixels.SelectMany(row => row));
            if (all == null) return null;
            var bands = new List<int[]>();
            for (var band = 0; band < Bands; band++)
            {
                // Rows from the bottom of the cover up.
                var top = SampleHeight - (band + 1) * SampleHeight / Bands;
                var bottom = SampleHeight - band * SampleHeight / Bands;
                bands.Add(Vivid(pixels.Skip(top).Take(bottom - top).SelectMany(row => row)) ?? all);
            }
            return bands;
        }

        // The cover shrunk to a few hundred pixels, rows top to bottom: plenty to tell its colours,
        // and the smoothing of the scaling irons out noise and text.
        private static Color[][] Sample(string path)
        {
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            using (var image = Image.FromStream(stream))
            using (var small = new Bitmap(SampleWidth, SampleHeight, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(image, 0, 0, SampleWidth, SampleHeight);
                }
                var rows = new Color[SampleHeight][];
                for (var y = 0; y < SampleHeight; y++)
                {
                    rows[y] = new Color[SampleWidth];
                    for (var x = 0; x < SampleWidth; x++) rows[y][x] = small.GetPixel(x, y);
                }
                return rows;
            }
        }

        // The hue that weighs most among the vivid pixels, as the weighted mean of its pixels
        // brought to full brightness; null when no pixel is vivid.
        private static int[] Vivid(IEnumerable<Color> pixels)
        {
            var weight = new double[HueBins];
            var r = new double[HueBins];
            var g = new double[HueBins];
            var b = new double[HueBins];
            foreach (var p in pixels)
            {
                var s = Saturation(p);
                var v = Math.Max(p.R, Math.Max(p.G, p.B)) / 255f;
                if (s < MinSaturation || v < MinValue) continue;
                var bin = (int)(p.GetHue() / 360f * HueBins) % HueBins;
                double w = s * v;
                weight[bin] += w;
                r[bin] += p.R * w;
                g[bin] += p.G * w;
                b[bin] += p.B * w;
            }
            var best = Array.IndexOf(weight, weight.Max());
            if (weight[best] <= 0) return null;
            var mean = Color.FromArgb((int)(r[best] / weight[best]), (int)(g[best] / weight[best]), (int)(b[best] / weight[best]));
            return FromHsv(mean.GetHue(), Math.Max(Saturation(mean), LightSaturation), 1f);
        }

        // HSV saturation: Color.GetSaturation is HSL's, which calls a dark red unsaturated.
        private static float Saturation(Color c)
        {
            var max = Math.Max(c.R, Math.Max(c.G, c.B));
            var min = Math.Min(c.R, Math.Min(c.G, c.B));
            return max == 0 ? 0 : (max - min) / (float)max;
        }

        private static int[] FromHsv(float hue, float saturation, float value)
        {
            var sector = hue / 60f;
            var i = (int)Math.Floor(sector) % 6;
            var f = sector - (float)Math.Floor(sector);
            var p = value * (1 - saturation);
            var q = value * (1 - f * saturation);
            var t = value * (1 - (1 - f) * saturation);
            float r, g, b;
            switch (i)
            {
                case 0: r = value; g = t; b = p; break;
                case 1: r = q; g = value; b = p; break;
                case 2: r = p; g = value; b = t; break;
                case 3: r = p; g = q; b = value; break;
                case 4: r = t; g = p; b = value; break;
                default: r = value; g = p; b = q; break;
            }
            return new[] { (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255) };
        }
    }
}
