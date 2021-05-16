using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;

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
        /// An optional predicate can be provided to skip dithering for certain colors, which can be used to prevent error diffusion from interfering with color-key transparency.
        /// </summary>
        public static byte[] FloydSteinberg(
            Image<Rgba32> image,
            Rgba32[] palette,
            Func<Rgba32, int> getColorIndex,
            float ditherScale = 1f,
            Func<Rgba32, bool> skipDithering = null)
        {
            var output = new byte[image.Width * image.Height];

            ditherScale = Math.Max(0f, Math.Min(ditherScale, 1f));
            var lastRowErrors = new int[image.Width, 3];
            var currentRowErrors = new int[image.Width, 3];
            for (int y = 0; y < image.Height; y++)
            {
                var rowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    var sourceColor = rowSpan[x];
                    if (skipDithering?.Invoke(sourceColor) == true)
                    {
                        currentRowErrors[x, 0] = 0;
                        currentRowErrors[x, 1] = 0;
                        currentRowErrors[x, 2] = 0;

                        output[y * image.Width + x] = (byte)getColorIndex(sourceColor);
                        continue;
                    }

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
                            if (x + 1 < image.Width)
                                error[i] += 0.1875f * lastRowErrors[x + 1, i];
                        }
                    }

                    var errorCorrectedColor = new Rgba32(
                        (byte)Math.Max(0, Math.Min(sourceColor.R + (int)error[0], 255)),
                        (byte)Math.Max(0, Math.Min(sourceColor.G + (int)error[1], 255)),
                        (byte)Math.Max(0, Math.Min(sourceColor.B + (int)error[2], 255)));

                    var paletteIndex = getColorIndex(errorCorrectedColor);
                    var outputColor = palette[paletteIndex];
                    currentRowErrors[x, 0] = (int)((sourceColor.R - outputColor.R) * ditherScale);
                    currentRowErrors[x, 1] = (int)((sourceColor.G - outputColor.G) * ditherScale);
                    currentRowErrors[x, 2] = (int)((sourceColor.B - outputColor.B) * ditherScale);

                    output[y * image.Width + x] = (byte)paletteIndex;
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
