using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas16bppRgb565 : Canvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format16bppRgb565;


        // [GGGBBBBB] [RRRRRGGG]
        public Canvas16bppRgb565(int width, int height, int stride, byte[] buffer)
            : base(width, height, stride, buffer)
        {
        }

        public override ColorARGB GetPixel(int x, int y)
        {
            var index = (y * Width + x) * 2;
            return new ColorARGB(
                (byte)(Buffer[index + 1] & 0xF8),
                (byte)(((Buffer[index + 1] & 0x07) << 5) | (((Buffer[index] & 0xE0) >> 5) << 3)),
                (byte)((Buffer[index] & 0x1F) << 3));
        }

        public override void SetPixel(int x, int y, ColorARGB color)
        {
            var index = (y * Width + x) * 2;
            Buffer[index++] = (byte)((color.B >> 3) | ((color.G >> 2) << 5));
            Buffer[index] = (byte)((color.G >> 5) | ((color.R >> 3) << 3));
        }
    }
}
