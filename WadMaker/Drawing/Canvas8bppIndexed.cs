using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas8bppIndexed : IndexedCanvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format8bppIndexed;


        public Canvas8bppIndexed(int width, int height, int stride, byte[] buffer, Color[] palette)
            : base(width, height, stride, buffer, palette)
        {
            if (palette.Length != 256)
                throw new ArgumentException($"Palette must contain exactly 256 colors.");
        }

        public override int GetIndex(int x, int y) => Buffer[y * Stride + x];

        public override void SetIndex(int x, int y, int index) => Buffer[y * Stride + x] = (byte)index;
    }
}
