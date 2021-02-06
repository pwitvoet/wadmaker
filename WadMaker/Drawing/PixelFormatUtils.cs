using System;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    internal static class PixelFormatUtils
    {
        /// <summary>
        /// Returns the number of bytes that are required per row, for the given width and pixel format.
        /// Strides are aligned to the next multiple of 4 bytes.
        /// </summary>
        public static int GetStride(int width, PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case PixelFormat.Format1bppIndexed:
                    return NearestMultipleOf4((width + 7) / 8);

                case PixelFormat.Format4bppIndexed:
                    return NearestMultipleOf4((width + 1) / 2);

                case PixelFormat.Format8bppIndexed:
                    return NearestMultipleOf4(width);

                case PixelFormat.Format16bppRgb555:
                case PixelFormat.Format16bppRgb565:
                case PixelFormat.Format16bppArgb1555:
                    return NearestMultipleOf4(width * 2);

                case PixelFormat.Format24bppRgb:
                    return NearestMultipleOf4(width * 3);

                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format32bppArgb:
                    return width * 4;

                default:
                    throw new NotSupportedException($"Cannot determine stride for pixel format {pixelFormat}.");
            }
        }

        /// <summary>
        /// Returns the number of colors that an indexed pixel format supports.
        /// Returns 0 for non-indexed pixel formats.
        /// </summary>
        public static int GetPaletteSize(PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case PixelFormat.Format1bppIndexed: return 2;
                case PixelFormat.Format4bppIndexed: return 16;
                case PixelFormat.Format8bppIndexed: return 256;
                default: return 0;
            }
        }


        private static int NearestMultipleOf4(int value) => ((value + 3) >> 2) << 2;
    }
}
