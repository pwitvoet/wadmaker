using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Shared.FileFormats
{
    public static class ImageReading
    {
        private static IImageReader[] _imageReaders;
        private static IDictionary<string, IImageReader> _extensionReaderMapping;

        static ImageReading()
        {
            _imageReaders = new IImageReader[] {
                new ImageReader(),
                new KraReader(),
                new PsdReader(),
            };

            _extensionReaderMapping = new Dictionary<string, IImageReader>();
            foreach (var reader in _imageReaders)
                foreach (var extension in reader.SupportedExtensions)
                    _extensionReaderMapping[extension] = reader;
        }


        public static bool IsSupported(string path)
        {
            return _extensionReaderMapping.ContainsKey(GetExtension(path));
        }

        public static Image<Rgba32> ReadImage(string path)
        {
            if (!_extensionReaderMapping.TryGetValue(GetExtension(path), out var reader))
                throw new NotSupportedException($"File format '{GetExtension(path)}' is not supported.");

            return reader.ReadImage(path);
        }


        private static string GetExtension(string path) => Path.GetExtension(path).TrimStart('.');
    }
}
