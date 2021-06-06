using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Shared.FileFormats
{
    /// <summary>
    /// An image reader for Krita (.kra) and OpenRaster (.ora) files.
    /// Works by reading the embedded 'mergedimage.png' file.
    /// </summary>
    public class KraReader : IImageReader
    {
        public string[] SupportedExtensions => new[] { "kra", "ora" };


        public Image<Rgba32> ReadImage(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read, true))
            {
                var mergedImageEntry = zip.Entries.FirstOrDefault(entry => entry.Name == "mergedimage.png");
                if (mergedImageEntry == null)
                    throw new InvalidDataException($"Could not find 'mergedimage.png' in '{path}'.");

                using (var entryStream = mergedImageEntry.Open())
                    return Image.Load<Rgba32>(entryStream);
            }
        }
    }
}
