using System;
using System.Collections.Generic;
using System.Linq;
using WadMaker.Drawing;

namespace WadMaker
{
    static class ColorQuantization
    {
        /// <summary>
        /// Creates a palette and a dictionary that maps input colors to their palette index.
        /// </summary>
        public static (ColorARGB[], IDictionary<ColorARGB, int>) CreatePaletteAndColorIndexMapping(
            IDictionary<ColorARGB, int> colorHistogram,
            int maxColors = 256,
            int volumeSelectionThreshold = 32)
        {
            var uniqueColors = colorHistogram.Keys.ToHashSet();
            if (uniqueColors.Count <= maxColors)
                return CreatePaletteAndMapping(uniqueColors.ToDictionary(color => color, color => new[] { color }));


            var boundingBoxes = new List<ColorBoundingBox>();
            boundingBoxes.Add(new ColorBoundingBox(uniqueColors));

            while (boundingBoxes.Count < maxColors)
            {
                // Pick the first few bounding boxes based on color count alone, but start taking volume into account after a while,
                // to give rarer (but notable) colors more of a chance:
                var boundingBox = (boundingBoxes.Count < volumeSelectionThreshold) ? boundingBoxes.OrderByDescending(box => box.Colors.Length).First() :
                                                                                     boundingBoxes.OrderByDescending(box => (long)box.Colors.Length * box.Volume).First();
                if (boundingBox.Colors.Length <= 1)
                    break;

                var sizeR = boundingBox.Max.R - boundingBox.Min.R;
                var sizeG = boundingBox.Max.G - boundingBox.Min.G;
                var sizeB = boundingBox.Max.B - boundingBox.Min.B;
                var middleR = boundingBox.Min.R + sizeR / 2;
                var middleG = boundingBox.Min.G + sizeG / 2;
                var middleB = boundingBox.Min.B + sizeB / 2;
                var isLow = (sizeR >= sizeG && sizeR >= sizeB) ? (Func<ColorARGB, bool>)(c => c.R <= middleR) :
                                          (sizeG >= sizeB) ?     (Func<ColorARGB, bool>)(c => c.G <= middleG) :
                                                                 (Func<ColorARGB, bool>)(c => c.B <= middleB);

                var lowColors = new List<ColorARGB>();
                var highColors = new List<ColorARGB>();
                foreach (var color in boundingBox.Colors)
                    (isLow(color) ? lowColors : highColors).Add(color);

                boundingBoxes.Remove(boundingBox);
                boundingBoxes.Add(new ColorBoundingBox(lowColors));
                boundingBoxes.Add(new ColorBoundingBox(highColors));
            }

            return CreatePaletteAndMapping(boundingBoxes.ToDictionary(box => box.GetWeightedAverageColor(colorHistogram), box => box.Colors));


            (ColorARGB[], IDictionary<ColorARGB, int>) CreatePaletteAndMapping(IDictionary<ColorARGB, ColorARGB[]> colorMappings)
            {
                var palette = new ColorARGB[colorMappings.Count];
                var colorIndexMapping = new Dictionary<ColorARGB, int>();

                var index = 0;
                foreach (var kv in colorMappings)
                {
                    palette[index] = kv.Key;
                    foreach (var color in kv.Value)
                        colorIndexMapping[color] = index;

                    index += 1;
                }

                return (palette, colorIndexMapping);
            }
        }

        /// <summary>
        /// Creates a lookup function that, for a given color, returns the index of the nearest color in the palette.
        /// Transparent colors are mapped to palette index 255.
        /// NOTE: The given color index mapping dictionary is used for memoization, and will be modified (no internal copy is created for performance reasons).
        /// </summary>
        public static Func<ColorARGB, int> CreateColorIndexLookup(ColorARGB[] palette, IDictionary<ColorARGB, int> colorIndexMapping, Func<ColorARGB, bool> isTransparent)
        {
            return color =>
            {
                if (isTransparent(color))
                    return 255;

                if (colorIndexMapping.TryGetValue(color, out var index))
                    return index;

                index = GetNearestColorIndex(palette, color);
                colorIndexMapping[color] = index;
                return index;
            };
        }

        /// <summary>
        /// Returns the index of the palette color that is closest to the given color, in RGB-space.
        /// </summary>
        public static int GetNearestColorIndex(ColorARGB[] palette, ColorARGB color)
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


        /// <summary>
        /// Returns the counts of all colors that are used in the given canvases.
        /// </summary>
        public static IDictionary<ColorARGB, int> GetColorHistogram(IEnumerable<IReadableCanvas> canvases, Func<ColorARGB, bool> skipColor = null)
        {
            var histogram = new Dictionary<ColorARGB, int>();
            foreach (var canvas in canvases)
            {
                for (int y = 0; y < canvas.Height; y++)
                {
                    for (int x = 0; x < canvas.Width; x++)
                    {
                        var color = canvas.GetPixel(x, y);
                        if (skipColor?.Invoke(color) == true)
                            continue;

                        if (!histogram.TryGetValue(color, out var count))
                            count = 0;
                        histogram[color] = count + 1;
                    }
                }
            }
            return histogram;
        }


        private static float SquaredDistance(ColorARGB a, ColorARGB b)
        {
            var dr = a.R - b.R;
            var dg = a.G - b.G;
            var db = a.B - b.B;
            return (dr * dr) + (dg * dg) + (db * db);
        }


        private class ColorBoundingBox
        {
            public ColorARGB[] Colors { get; }
            public ColorARGB Min { get; }
            public ColorARGB Max { get; }

            public int Volume => (Max.R - Min.R) * (Max.G - Min.G) * (Max.B - Min.B);

            public ColorBoundingBox(IEnumerable<ColorARGB> colors)
            {
                Colors = colors.ToArray();
                if (!Colors.Any())
                    throw new ArgumentException("At least one color must be provided.", nameof(colors));

                (Min, Max) = GetMinMaxColors(Colors);
            }

            public ColorARGB GetWeightedAverageColor(IDictionary<ColorARGB, int> colorHistogram)
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
                return new ColorARGB((byte)(r / totalWeight), (byte)(g / totalWeight), (byte)(b / totalWeight));
            }


            private static (ColorARGB, ColorARGB) GetMinMaxColors(IEnumerable<ColorARGB> colors)
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
                return (new ColorARGB(minR, minG, minB), new ColorARGB(maxR, maxG, maxB));
            }
        }
    }
}
