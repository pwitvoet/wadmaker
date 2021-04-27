﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using WadMaker.Drawing;

namespace WadMaker
{
    enum DitheringAlgorithm
    {
        None,

        FloydSteinberg,
    }


    static class Dithering
    {
        /// <summary>
        /// Uses Floyd-Steinberg dithering to create an 8-bit indexed canvas from the input canvas and the given palette.
        /// Error diffusion can be limited to make the dithering effect more subtle.
        /// </summary>
        public static IIndexedCanvas FloydSteinberg(IReadableCanvas canvas, Color[] palette, Func<Color, int> colorIndexLookup, int maxErrorDiffusion = 255)
        {
            var output = IndexedCanvas.Create(canvas.Width, canvas.Height, PixelFormat.Format8bppIndexed, palette);

            var lastRowErrors = new int[canvas.Width, 3];
            var currentRowErrors = new int[canvas.Width, 3];
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    // Error diffusion:
                    // 1/16  |  5/16  |  3/16
                    // 7/16  | (x, y) |
                    var error = new float[3];
                    for (int i = 0; i < 3; i++)
                    {
                        if (x > 0)
                        {
                            error[i] += 0.4375f * currentRowErrors[x - 1, i];
                            if (y > 0)
                                error[i] += 0.0625f * lastRowErrors[x - 1, i];
                        }

                        if (y > 0)
                        {
                            error[i] += 0.3125f * lastRowErrors[x, i];
                            if (x + 1 < canvas.Width)
                                error[i] += 0.1875f * lastRowErrors[x + 1, i];
                        }
                    }

                    var sourceColor = canvas.GetPixel(x, y);
                    var errorCorrectedColor = Color.FromArgb(
                        Math.Max(0, Math.Min(sourceColor.R + (int)error[0], 255)),
                        Math.Max(0, Math.Min(sourceColor.G + (int)error[1], 255)),
                        Math.Max(0, Math.Min(sourceColor.B + (int)error[2], 255)));

                    var paletteIndex = colorIndexLookup(errorCorrectedColor);
                    var outputColor = palette[paletteIndex];
                    currentRowErrors[x, 0] = Math.Max(-maxErrorDiffusion, Math.Min(sourceColor.R - outputColor.R, maxErrorDiffusion));
                    currentRowErrors[x, 1] = Math.Max(-maxErrorDiffusion, Math.Min(sourceColor.G - outputColor.G, maxErrorDiffusion));
                    currentRowErrors[x, 2] = Math.Max(-maxErrorDiffusion, Math.Min(sourceColor.B - outputColor.B, maxErrorDiffusion));

                    output.SetIndex(x, y, paletteIndex);
                }

                // Swapping the error rows is sufficient - the new current row will be overwritten as we go:
                var temp = lastRowErrors;
                lastRowErrors = currentRowErrors;
                currentRowErrors = temp;
            }

            return output;
        }
    }
}