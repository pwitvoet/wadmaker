using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    class Canvas32bppArgb : Canvas
    {
        public override PixelFormat PixelFormat => PixelFormat.Format32bppArgb;


        // [BBBBBBBB] [GGGGGGGG] [RRRRRRRR] [AAAAAAAA]
        public Canvas32bppArgb(int width, int height, int stride, byte[] buffer)
            : base(width, height, stride, buffer)
        {
        }

        public override ColorARGB GetPixel(int x, int y)
        {
            var index = (y * Width + x) * 4;
            return new ColorARGB(Buffer[index + 3], Buffer[index + 2], Buffer[index + 1], Buffer[index]);
        }

        public override void SetPixel(int x, int y, ColorARGB color)
        {
            var index = (y * Width + x) * 4;
            Buffer[index++] = color.B;
            Buffer[index++] = color.G;
            Buffer[index++] = color.R;
            Buffer[index] = color.A;
        }
    }
}
