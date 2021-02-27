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
        /// Creates indexed canvases from the given canvases, by deriving a color palette from the colors in the given canvases.
        /// All output canvases will share the same palette.
        /// </summary>
        public static IEnumerable<IIndexedCanvas> CreateIndexedCanvases(IEnumerable<IReadableCanvas> canvases, bool hasTransparency)
        {
            // First gather all the unique colors from the input canvases:
            var inputCanvases = canvases.ToArray();
            var uniqueColors = inputCanvases
                .Select(CanvasExtensions.GetColorHistogram)
                .SelectMany(histogram => histogram.Keys)
                .ToHashSet();

            if (hasTransparency)
                uniqueColors.RemoveWhere(IsTransparent);


            // Then create a suitable palette (if transparency is enabled, then the final palette slot will be reserved for transparent pixels):
            (var palette, var paletteIndexLookup) = CreatePaletteAndIndexLookup(uniqueColors, hasTransparency ? 255 : 256);

            if (palette.Length < 256)
                palette = palette.Concat(Enumerable.Repeat(Color.FromArgb(0, 0, 0), 256 - palette.Length)).ToArray();

            if (hasTransparency)
                palette[255] = Color.FromArgb(0, 0, 255);   // Make the transparent color deep blue, by convention.


            // Finally, convert the input canvases, applying the palette that was just created:
            return inputCanvases
                .Select(canvas =>
                {
                    var outputCanvas = IndexedCanvas.Create(canvas.Width, canvas.Height, PixelFormat.Format8bppIndexed, palette);
                    for (int y = 0; y < canvas.Height; y++)
                    {
                        for (int x = 0; x < canvas.Width; x++)
                        {
                            var color = canvas.GetPixel(x, y);
                            var paletteIndex = (hasTransparency && IsTransparent(color)) ? 255 : paletteIndexLookup(color);
                            outputCanvas.SetIndex(x, y, paletteIndex);
                        }
                    }
                    return outputCanvas;
                })
                .ToArray();
        }


        /// <summary>
        /// Creates a palette from the given color histogram, along with a lookup function that maps colors that occur in the histogram to a palette index.
        /// This uses a median-cut algorithm.
        /// </summary>
        private static (Color[], Func<Color, int>) CreatePaletteAndIndexLookup(HashSet<Color> uniqueColors, int maxColors = 256)
        {
            if (uniqueColors.Count <= maxColors)
                return CreatePaletteAndLookup(uniqueColors.ToDictionary(color => color, color => new[] { color }));


            var boundingBoxes = new List<ColorBoundingBox>();
            boundingBoxes.Add(new ColorBoundingBox(uniqueColors));

            while (boundingBoxes.Count < maxColors)
            {
                var boundingBox = boundingBoxes.OrderByDescending(box => box.Colors.Length).First();
                if (boundingBox.Colors.Length <= 1)
                    break;

                var sizeR = boundingBox.Max.R - boundingBox.Min.R;
                var sizeG = boundingBox.Max.G - boundingBox.Min.G;
                var sizeB = boundingBox.Max.B - boundingBox.Min.B;
                var middleR = boundingBox.Min.R + sizeR / 2;
                var middleG = boundingBox.Min.G + sizeG / 2;
                var middleB = boundingBox.Min.B + sizeB / 2;
                var isLow = (sizeR >= sizeG && sizeR >= sizeB) ? (Func<Color, bool>)(c => c.R <= middleR) :
                            (sizeG >= sizeB) ?                   (Func<Color, bool>)(c => c.G <= middleG) :
                                                                 (Func<Color, bool>)(c => c.B <= middleB);

                var lowColors = new List<Color>();
                var highColors = new List<Color>();
                foreach (var color in boundingBox.Colors)
                    (isLow(color) ? lowColors : highColors).Add(color);

                boundingBoxes.Remove(boundingBox);
                boundingBoxes.Add(new ColorBoundingBox(lowColors));
                boundingBoxes.Add(new ColorBoundingBox(highColors));
            }

            return CreatePaletteAndLookup(boundingBoxes.ToDictionary(box => box.GetAverageColor(), box => box.Colors));


            (Color[], Func<Color, int>) CreatePaletteAndLookup(IDictionary<Color, Color[]> colorMappings)
            {
                var palette = new Color[colorMappings.Count];
                var indexLookup = new Dictionary<Color, int>();

                var index = 0;
                foreach (var kv in colorMappings)
                {
                    palette[index] = kv.Key;
                    foreach (var color in kv.Value)
                        indexLookup[color] = index;

                    index += 1;
                }

                return (palette, color => indexLookup[color]);
            }
        }

        private static bool IsTransparent(Color color) => color.A < 128;


        private class ColorBoundingBox
        {
            public Color[] Colors { get; }
            public Color Min { get; }
            public Color Max { get; }

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
