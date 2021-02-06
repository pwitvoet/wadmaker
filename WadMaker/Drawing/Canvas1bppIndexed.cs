using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas1bppIndexed : IndexedCanvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format1bppIndexed;


        public Canvas1bppIndexed(int width, int height, int stride, byte[] buffer, Color[] palette)
            : base(width, height, stride, buffer, palette)
        {
            if (palette.Length != 2)
                throw new ArgumentException($"Palette must contain exactly 2 colors.");
        }

        public override int GetIndex(int x, int y)
        {
            var byteIndex = y * Stride + (x >> 3);
            return (Buffer[byteIndex] >> (7 - (x & 0x07))) & 1;
        }

        public override void SetIndex(int x, int y, int index)
        {
            var byteIndex = y * Stride + (x >> 3);
            var bitMask = (byte)(0x80 >> (x & 0x07));
            if (index == 0)
                Buffer[byteIndex] &= (byte)~bitMask;
            else
                Buffer[byteIndex] |= bitMask;
        }
    }
}
