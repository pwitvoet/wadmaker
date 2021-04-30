﻿using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas24bppRgb : Canvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format24bppRgb;


        // [BBBBBBBB] [GGGGGGGG] [RRRRRRRR]
        public Canvas24bppRgb(int width, int height, int stride, byte[] buffer)
            : base(width, height, stride, buffer)
        {
        }

        public override ColorARGB GetPixel(int x, int y)
        {
            var index = (y * Width + x) * 3;
            return new ColorARGB(Buffer[index + 2], Buffer[index + 1], Buffer[index]);
        }

        public override void SetPixel(int x, int y, ColorARGB color)
        {
            var index = (y * Width + x) * 3;
            Buffer[index++] = color.B;
            Buffer[index++] = color.G;
            Buffer[index] = color.R;
        }
    }
}
