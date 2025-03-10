﻿using SixLabors.ImageSharp.PixelFormats;

namespace Shared.FileFormats.Indexed
{
    public class IndexedImage
    {
        public int Width { get; }
        public int Height { get; }
        public Rgba32[] Palette { get; }

        public byte[] ImageData => Frames[0].ImageData;

        public List<IndexedImageFrame> Frames { get; } = new();


        public IndexedImage(int width, int height, Rgba32[] palette)
        {
            Width = width;
            Height = height;
            Palette = palette;
        }

        public IndexedImage(byte[] imageData, int width, int height, Rgba32[] palette)
            : this(width, height, palette)
        {
            if (imageData.Length != width * height)
                throw new ArgumentException($"Image data must be {width} * {height}.");

            Frames.Add(new IndexedImageFrame(imageData, width, height));
        }

        public byte this[int x, int y]
        {
            get => Frames[0][x, y];
            set => Frames[0][x, y] = value;
        }
    }
}
