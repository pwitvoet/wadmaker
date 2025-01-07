using SixLabors.ImageSharp.PixelFormats;

namespace Shared.FileFormats.Indexed
{
    public class IndexedImage
    {
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<Rgba32> Palette { get; }

        private byte[] ImageData { get; }


        public IndexedImage(byte[] imageData, int width, int height, Rgba32[] palette)
        {
            if (imageData.Length != width * height)
                throw new ArgumentException($"Image data must be {width} * {height}.");

            ImageData = imageData;
            Width = width;
            Height = height;
            Palette = palette;
        }

        public byte this[int x, int y]
        {
            get => ImageData[y * Width + x];
        }
    }
}
