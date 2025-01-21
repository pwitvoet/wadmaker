namespace Shared.FileFormats.Indexed
{
    public class IndexedImageFrame
    {
        public int Width { get; }
        public int Height { get; }

        public byte[] ImageData { get; }


        public IndexedImageFrame(byte[] imageData, int width, int height)
        {
            if (imageData.Length != width * height)
                throw new ArgumentException($"Image data must be {width} * {height}.");

            ImageData = imageData;
            Width = width;
            Height = height;
        }

        public byte this[int x, int y]
        {
            get => ImageData[y * Width + x];
            set => ImageData[y * Width + x] = value;
        }
    }
}
