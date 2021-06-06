using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Linq;

namespace Shared.FileFormats
{
    /// <summary>
    /// An image reader for common image formats (.png, .jpg, .gif, .bmp and .tga).
    /// </summary>
    public class ImageReader : IImageReader
    {
        public string[] SupportedExtensions => Configuration.Default.ImageFormats
            .SelectMany(format => format.FileExtensions)
            .ToArray();


        public Image<Rgba32> ReadImage(string path)
        {
            return Image.Load<Rgba32>(path);
        }
    }
}
