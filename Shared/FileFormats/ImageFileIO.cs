using Shared.FileFormats.Indexed;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using System.Buffers.Binary;

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
                new PdnReader(),
            };

            _extensionReaderMapping = new Dictionary<string, IImageReader>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var reader in _imageReaders)
                foreach (var extension in reader.SupportedExtensions)
                    _extensionReaderMapping[extension] = reader;
        }


        /// <summary>
        /// Returns the default extension for the given image format, without a leading dot.
        /// </summary>
        public static string GetDefaultExtension(ImageFormat format)
        {
            switch (format)
            {
                case ImageFormat.Png: return "png";
                case ImageFormat.Jpg: return "jpg";
                case ImageFormat.Gif: return "gif";
                case ImageFormat.Bmp: return "bmp";
                case ImageFormat.Tga: return "tga";
                default: throw new NotSupportedException($"Unknown image file format: {format}.");
            }
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

        public static bool IsIndexed(string path) => IndexedImageReader.IsIndexed(path);

        public static IndexedImage LoadIndexedImage(string path) => IndexedImageReader.LoadIndexedImage(path);


        /// <summary>
        /// Saves the given image in the specified format.
        /// </summary>
        public static void SaveImage(Image<Rgba32> image, string path, ImageFormat format)
            => image.Save(path, GetImageEncoder(format));

        /// <summary>
        /// Saves the given indexed image in the specified format.
        /// An exception will be thrown if the format does not support indexed images.
        /// </summary>
        public static void SaveIndexedImage(IndexedImage indexedImage, string path, ImageFormat format)
        {
            // NOTE: The custom 'quantizer' contains the actual image data and palette, but we still need an Image instance so we can use its Save method:
            using (var indexedImageSavingQuantizer = new IndexedImageSavingQuantizer(indexedImage))
            using (var dummyImage = new Image<Rgba32>(indexedImage.Width, indexedImage.Height))
            {
                var imageEncoder = GetIndexedImageEncoder(format, indexedImageSavingQuantizer);
                dummyImage.Save(path, imageEncoder);
            }

            if (format == ImageFormat.Bmp)
                SetClrUsedByte(path, indexedImage.Palette.Length);
        }


        private static string GetExtension(string path) => Path.GetExtension(path).TrimStart('.');

        private static ImageEncoder GetImageEncoder(ImageFormat format)
        {
            switch (format)
            {
                default:
                case ImageFormat.Png: return new PngEncoder();
                case ImageFormat.Jpg: return new JpegEncoder();
                case ImageFormat.Gif: return new GifEncoder();
                case ImageFormat.Bmp: return new BmpEncoder();
                case ImageFormat.Tga: return new TgaEncoder { BitsPerPixel = TgaBitsPerPixel.Pixel32 };
            }
        }

        private static QuantizingImageEncoder GetIndexedImageEncoder(ImageFormat format, IQuantizer quantizer)
        {
            switch (format)
            {
                case ImageFormat.Png: return new PngEncoder {
                    BitDepth = PngBitDepth.Bit8,
                    ColorType = PngColorType.Palette,
                    Quantizer = quantizer,
                };

                case ImageFormat.Gif: return new GifEncoder {
                    ColorTableMode = GifColorTableMode.Global,
                    Quantizer = quantizer,
                };

                case ImageFormat.Bmp: return new BmpEncoder {
                    BitsPerPixel = BmpBitsPerPixel.Pixel8,
                    Quantizer = quantizer,
                };

                default: throw new NotSupportedException($"{format} format does not support indexed images.");
            }
        }

        //Tony; the gameui bitmap loader requires the biClrUsed property to actually be set; but ImageSharp is forcing it to 0.
        // https://github.com/SixLabors/ImageSharp/blob/v3.1.11/src/ImageSharp/Formats/Bmp/BmpEncoderCore.cs#L210-L220
        private static void SetClrUsedByte(string path, int paletteSize)
        {
            if (paletteSize <= 0)
                return;

            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length < 54)
                return;

            Span<byte> header = stackalloc byte[54];
            var read = stream.Read(header);
            if (read < header.Length)
                return;

            if (header[0] != (byte)'B' || header[1] != (byte)'M')
                return;

            var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(14, 4));
            if (dibHeaderSize < 40)
                return;

            var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(28, 2));
            if (bitsPerPixel is not (1 or 2 or 4 or 8))
                return;

            var maxColors = 1 << bitsPerPixel;
            var clrUsed = (uint)Math.Clamp(paletteSize, 1, maxColors);

            Span<byte> clrUsedBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(clrUsedBytes, clrUsed);
            stream.Seek(46, SeekOrigin.Begin);
            stream.Write(clrUsedBytes);
        }
    }
}
