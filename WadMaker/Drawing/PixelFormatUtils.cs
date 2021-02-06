using System;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    internal static class PixelFormatUtils
    {
        /// <summary>
        /// Returns the number of bytes that are required per row, for the given width and pixel format.
        /// By default, strides are aligned to the next multiple of 4 bytes.
        /// </summary>
        public static int GetStride(int width, PixelFormat pixelFormat, bool aligned = true)
        {
            var stride = pixelFormat switch {
                PixelFormat.Format1bppIndexed => (width + 7) / 8,
                PixelFormat.Format4bppIndexed => (width + 1) / 2,
                PixelFormat.Format8bppIndexed => width,
                PixelFormat.Format16bppRgb555 => width * 2,
                PixelFormat.Format16bppRgb565 => width * 2,
                PixelFormat.Format16bppArgb1555 => width * 2,
                PixelFormat.Format24bppRgb => width * 3,
                PixelFormat.Format32bppRgb => width * 4,
                PixelFormat.Format32bppArgb => width * 4,
                _ => throw new NotSupportedException($"Cannot determine stride for pixel format {pixelFormat}."),
            };
            return aligned ? NearestMultipleOf4(stride) : stride;
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
