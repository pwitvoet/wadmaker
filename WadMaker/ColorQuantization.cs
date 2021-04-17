using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using WadMaker.Drawing;

namespace WadMaker
{
    static class ColorQuantization
    {
        /// <summary>
        /// Creates a palette and a dictionary that maps input colors to their palette index.
        /// </summary>
        public static (Color[], IDictionary<Color, int>) CreatePaletteAndColorIndexMapping(HashSet<Color> uniqueColors, int maxColors = 256, int volumeSelectionTreshold = 32)
        {
            if (uniqueColors.Count <= maxColors)
                return CreatePaletteAndMapping(uniqueColors.ToDictionary(color => color, color => new[] { color }));


            var boundingBoxes = new List<ColorBoundingBox>();
            boundingBoxes.Add(new ColorBoundingBox(uniqueColors));

            while (boundingBoxes.Count < maxColors)
            {
                // Pick the first few bounding boxes based on color count alone, but start taking volume into account after a while,
                // to give rarer (but notable) colors more of a chance:
                var boundingBox = (boundingBoxes.Count < volumeSelectionTreshold) ? boundingBoxes.OrderByDescending(box => box.Colors.Length).First() :
                                                                                    boundingBoxes.OrderByDescending(box => (long)box.Colors.Length * box.Volume).First();
                if (boundingBox.Colors.Length <= 1)
                    break;

                var sizeR = boundingBox.Max.R - boundingBox.Min.R;
                var sizeG = boundingBox.Max.G - boundingBox.Min.G;
                var sizeB = boundingBox.Max.B - boundingBox.Min.B;
                var middleR = boundingBox.Min.R + sizeR / 2;
                var middleG = boundingBox.Min.G + sizeG / 2;
                var middleB = boundingBox.Min.B + sizeB / 2;
                var isLow = (sizeR >= sizeG && sizeR >= sizeB) ? (Func<Color, bool>)(c => c.R <= middleR) :
                                          (sizeG >= sizeB) ?     (Func<Color, bool>)(c => c.G <= middleG) :
                                                                 (Func<Color, bool>)(c => c.B <= middleB);

                var lowColors = new List<Color>();
                var highColors = new List<Color>();
                foreach (var color in boundingBox.Colors)
                    (isLow(color) ? lowColors : highColors).Add(color);

                boundingBoxes.Remove(boundingBox);
                boundingBoxes.Add(new ColorBoundingBox(lowColors));
                boundingBoxes.Add(new ColorBoundingBox(highColors));
            }

            return CreatePaletteAndMapping(boundingBoxes.ToDictionary(box => box.GetAverageColor(), box => box.Colors));


            (Color[], IDictionary<Color, int>) CreatePaletteAndMapping(IDictionary<Color, Color[]> colorMappings)
            {
                var palette = new Color[colorMappings.Count];
                var colorIndexMapping = new Dictionary<Color, int>();

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
        public static Func<Color, int> CreateColorIndexLookup(Color[] palette, IDictionary<Color, int> colorIndexMapping, Func<Color, bool> isTransparent)
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
        public static int GetNearestColorIndex(Color[] palette, Color color)
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

        public static bool IsTransparent(Color color) => color.A < 128;


        private static float SquaredDistance(Color a, Color b)
        {
            var dr = a.R - b.R;
            var dg = a.G - b.G;
            var db = a.B - b.B;
            return (dr * dr) + (dg * dg) + (db * db);
        }


        private class ColorBoundingBox
        {
            public Color[] Colors { get; }
            public Color Min { get; }
            public Color Max { get; }

            public int Volume => (Max.R - Min.R) * (Max.G - Min.G) * (Max.B - Min.B);

            public ColorBoundingBox(IEnumerable<Color> colors)
            {
                Colors = colors.ToArray();
                if (!Colors.Any())
                    throw new ArgumentException("At least one color must be provided.", nameof(colors));

                (Min, Max) = GetMinMaxColors(Colors);
            }

            public Color GetAverageColor()
            {
                long r = 0;
                long g = 0;
                long b = 0;
                foreach (var color in Colors)
                {
                    r += color.R;
                    g += color.G;
                    b += color.B;
                }
                return Color.FromArgb((byte)(r / Colors.Length), (byte)(g / Colors.Length), (byte)(b / Colors.Length));
            }


            private static (Color, Color) GetMinMaxColors(IEnumerable<Color> colors)
            {
                var minR = 255;
                var minG = 255;
                var minB = 255;
                var maxR = 0;
                var maxG = 0;
                var maxB = 0;
                foreach (var color in colors)
                {
                    minR = Math.Min(minR, color.R);
                    minG = Math.Min(minG, color.G);
                    minB = Math.Min(minB, color.B);
                    maxR = Math.Max(maxR, color.R);
                    maxG = Math.Max(maxG, color.G);
                    maxB = Math.Max(maxB, color.B);
                }
                return (Color.FromArgb(minR, minG, minB), Color.FromArgb(maxR, maxG, maxB));
            }
        }
    }
}
