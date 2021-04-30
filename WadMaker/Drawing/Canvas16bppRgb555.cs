using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas16bppRgb555 : Canvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format16bppRgb555;


        // [GGGBBBBB] [.RRRRRGG]
        public Canvas16bppRgb555(int width, int height, int stride, byte[] buffer)
            : base(width, height, stride, buffer)
        {
        }

        public override ColorARGB GetPixel(int x, int y)
        {
            var index = (y * Width + x) * 2;
            return new ColorARGB(
                (byte)(((Buffer[index + 1] & 0x7C) >> 2) << 3),
                (byte)(((Buffer[index + 1] & 0x03) << 6) | (((Buffer[index] & 0xE0) >> 5) << 3)),
                (byte)((Buffer[index] & 0x1F) << 3));
        }

        public override void SetPixel(int x, int y, ColorARGB color)
        {
            var index = (y * Width + x) * 2;
            Buffer[index++] = (byte)((color.B >> 3) | ((color.G >> 3) << 5));
            Buffer[index] = (byte)((color.G >> 6) | ((color.R >> 3) << 2));
        }
    }
}
