using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WadMaker
{
    static class ColorQuantization
    {
        /// <summary>
        /// Divides the colors in the given histogram into clusters, using a modified median-cut algorithm.
        /// Returns an array of (average-color, nearby-colors) tuples.
        /// </summary>
        public static (Rgba32, Rgba32[])[] GetColorClusters(
            IDictionary<Rgba32, int> colorHistogram,
            int maxColors = 256)
        {
            var uniqueColors = colorHistogram.Keys.ToHashSet();
            if (uniqueColors.Count <= maxColors)
            {
                return uniqueColors
                    .Select(color => (color, new[] { color }))
                    .ToArray();
            }


            var boundingBoxes = new List<ColorBoundingBox>();
            boundingBoxes.Add(new ColorBoundingBox(uniqueColors));

            while (boundingBoxes.Count < maxColors)
            {
                // Pick the first few bounding boxes based on color count alone, but start taking volume into account after a while,
                // to give rarer (but notable) colors more of a chance (see http://leptonica.org/papers/mediancut.pdf ):
                var boundingBox = (boundingBoxes.Count < maxColors / 2) ? boundingBoxes.OrderByDescending(box => box.Colors.Length).First() :
                                                                          boundingBoxes.OrderByDescending(box => (long)box.Colors.Length * box.Volume).First();
                if (boundingBox.Colors.Length <= 1)
                    break;

                var sizeR = boundingBox.Max.R - boundingBox.Min.R;
                var sizeG = boundingBox.Max.G - boundingBox.Min.G;
                var sizeB = boundingBox.Max.B - boundingBox.Min.B;
                var middleR = boundingBox.Min.R + sizeR / 2;
                var middleG = boundingBox.Min.G + sizeG / 2;
                var middleB = boundingBox.Min.B + sizeB / 2;
                var isLow = (sizeR >= sizeG && sizeR >= sizeB) ? (Func<Rgba32, bool>)(c => c.R <= middleR) :
                                              (sizeG >= sizeB) ? (Func<Rgba32, bool>)(c => c.G <= middleG) :
                                                                 (Func<Rgba32, bool>)(c => c.B <= middleB);

                var lowColors = new List<Rgba32>();
                var highColors = new List<Rgba32>();
                foreach (var color in boundingBox.Colors)
                    (isLow(color) ? lowColors : highColors).Add(color);

                boundingBoxes.Remove(boundingBox);
                boundingBoxes.Add(new ColorBoundingBox(lowColors));
                boundingBoxes.Add(new ColorBoundingBox(highColors));
            }

            return boundingBoxes
                .Select(box => (box.GetWeightedAverageColor(colorHistogram), box.Colors))
                .ToArray();
        }

        /// <summary>
        /// Returns the counts of all colors that are used in the given images.
        /// </summary>
        public static IDictionary<Rgba32, int> GetColorHistogram(IEnumerable<Image<Rgba32>> images, Func<Rgba32, bool> ignoreColor)
        {
            var colorHistogram = new Dictionary<Rgba32, int>();

            foreach (var image in images)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = image.GetPixelRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var color = rowSpan[x];
                        if (ignoreColor(color))
                            continue;

                        if (!colorHistogram.TryGetValue(color, out var count))
                            count = 0;

                        colorHistogram[color] = count + 1;
                    }
                }
            }

            return colorHistogram;
        }

        /// <summary>
        /// Creates a lookup function that, for a given color, returns the index of the nearest color in the palette.
        /// Transparent colors are mapped to palette index 255.
        /// NOTE: The given color index mapping dictionary is used for memoization, and will be modified (no internal copy is created for performance reasons).
        /// </summary>
        public static Func<Rgba32, int> CreateColorIndexLookup(Rgba32[] palette, IDictionary<Rgba32, int> colorIndexMappingCache, Func<Rgba32, bool> isTransparent)
        {
            return color =>
            {
                if (isTransparent(color))
                    return 255;

                if (colorIndexMappingCache.TryGetValue(color, out var index))
                    return index;

                index = GetNearestColorIndex(palette, color);
                colorIndexMappingCache[color] = index;
                return index;
            };
        }

        /// <summary>
        /// Returns the index of the palette color that is closest to the given color, in RGB-space.
        /// </summary>
        public static int GetNearestColorIndex(Rgba32[] palette, Rgba32 color)
        {
            var index = 0;
            var minSquaredDistance = float.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                var squaredDistance = SquaredDistance(palette[i], color);
                if (squaredDistance < minSquaredDistance)
                {
                    minSquaredDistance = squaredDistance;
                    index = i;
                }
            }
            return index;
        }


        private static float SquaredDistance(Rgba32 color1, Rgba32 color2)
        {
            var dr = color1.R - color2.R;
            var dg = color1.G - color2.G;
            var db = color1.B - color2.B;
            return (dr * dr) + (dg * dg) + (db * db);
        }


        private class ColorBoundingBox
        {
            public Rgba32[] Colors { get; }
            public Rgba32 Min { get; }
            public Rgba32 Max { get; }

            public int Volume => (Max.R - Min.R) * (Max.G - Min.G) * (Max.B - Min.B);

            public ColorBoundingBox(IEnumerable<Rgba32> colors)
            {
                Colors = colors.ToArray();
                if (!Colors.Any())
                    throw new ArgumentException("At least one color must be provided.", nameof(colors));

                (Min, Max) = GetMinMaxColors(Colors);
            }

            public Rgba32 GetWeightedAverageColor(IDictionary<Rgba32, int> colorHistogram)
            {
                long r = 0;
                long g = 0;
                long b = 0;
                long totalWeight = 0;
                foreach (var color in Colors)
                {
                    if (colorHistogram.TryGetValue(color, out var weight))
                    {
                        r += color.R * weight;
                        g += color.G * weight;
                        b += color.B * weight;
                        totalWeight += weight;
                    }
                }
                return new Rgba32((byte)(r / totalWeight), (byte)(g / totalWeight), (byte)(b / totalWeight));
            }


            private static (Rgba32, Rgba32) GetMinMaxColors(IEnumerable<Rgba32> colors)
            {
                byte minR = 255;
                byte minG = 255;
                byte minB = 255;
                byte maxR = 0;
                byte maxG = 0;
                byte maxB = 0;
                foreach (var color in colors)
                {
                    minR = Math.Min(minR, color.R);
                    minG = Math.Min(minG, color.G);
                    minB = Math.Min(minB, color.B);
                    maxR = Math.Max(maxR, color.R);
                    maxG = Math.Max(maxG, color.G);
                    maxB = Math.Max(maxB, color.B);
                }
                return (new Rgba32(minR, minG, minB), new Rgba32(maxR, maxG, maxB));
            }
        }
    }
}
