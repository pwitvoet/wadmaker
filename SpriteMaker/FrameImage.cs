using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;

namespace SpriteMaker
{
    class FrameImage : IDisposable
    {
        public Image<Rgba32> Image { get; }
        public SpriteSettings Settings { get; }
        public int FrameNumber { get; }


        public FrameImage(Image<Rgba32> image, SpriteSettings settings, int frameNumber)
        {
            Image = image;
            Settings = settings;
            FrameNumber = frameNumber;
        }

        public void Dispose()
        {
            Image?.Dispose();
        }
    }
}
