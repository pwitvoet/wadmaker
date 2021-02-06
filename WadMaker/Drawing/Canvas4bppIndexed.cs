using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas4bppIndexed : IndexedCanvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format4bppIndexed;


        public Canvas4bppIndexed(int width, int height, int stride, byte[] buffer, Color[] palette)
            : base(width, height, stride, buffer, palette)
        {
            if (palette.Length != 16)
                throw new ArgumentException($"Palette must contain exactly 16 colors.");
        }

        public override int GetIndex(int x, int y)
        {
            var byteIndex = y * Stride + (x >> 1);
            return (x & 0x01) == 0 ? Buffer[byteIndex] >> 4 : Buffer[byteIndex] & 0x0F;
        }

        public override void SetIndex(int x, int y, int index)
        {
            var byteIndex = y * Stride + (x >> 1);
            if ((x & 0x01) == 0)
                Buffer[byteIndex] = (byte)((Buffer[byteIndex] & 0x0F) | ((index & 0x0F) << 4));
            else
                Buffer[byteIndex] = (byte)((Buffer[byteIndex] & 0xF0) | (index & 0x0F));
        }
    }
}
