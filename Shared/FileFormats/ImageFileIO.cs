using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Shared.FileFormats
{
    public static class ImageFileIO
    {
        private static IImageReader[] _imageReaders;
        private static IDictionary<string, IImageReader> _extensionReaderMapping;

        static ImageFileIO()
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


        /// <summary>
        /// Returns true if the given file type (based on extension) is supported.
        /// </summary>
        public static bool CanLoad(string path) => _extensionReaderMapping.ContainsKey(GetExtension(path));

        /// <summary>
        /// Loads the given image file.
        /// This method will throw an exception if the file format is not supported, or if loading fails for some other reason.
        /// </summary>
        public static Image<Rgba32> LoadImage(string path)
        {
            if (!_extensionReaderMapping.TryGetValue(GetExtension(path), out var reader))
                throw new NotSupportedException($"File format '{GetExtension(path)}' is not supported.");

            return reader.ReadImage(path);
        }


        private static string GetExtension(string path) => Path.GetExtension(path).TrimStart('.');
    }
}
