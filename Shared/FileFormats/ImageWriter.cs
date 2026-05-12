using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using Shared.FileFormats.Indexed;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace Shared.FileFormats
{
    /// <summary>
    /// An image writer for common image formats (.png, .jpg, .gif, .bmp and .tga).
    /// </summary>
    public class ImageWriter
    {
        public void WriteImage(Image<Rgba32> image, string path, ImageFormat format)
            => image.Save(path, GetImageEncoder(format));

        public void WriteIndexedImage(IndexedImage indexedImage, string path, ImageFormat format)
        {
            if (format == ImageFormat.Bmp)
            {
                using (var memoryStream = new MemoryStream())
                {
                    WriteIndexedImage(indexedImage, memoryStream, format);
                    SetBitmapColorsUsedField(memoryStream, indexedImage.Palette.Length);

                    using (var file = File.Create(path))
                    {
                        memoryStream.Position = 0;
                        memoryStream.CopyTo(file);
                    }
                }
            }
            else
            {
                using (var file = File.Create(path))
                    WriteIndexedImage(indexedImage, file, format);
            }
        }


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

        private static void WriteIndexedImage(IndexedImage indexedImage, Stream stream, ImageFormat format)
        {
            // NOTE: The custom 'quantizer' contains the actual image data and palette, but we still need an Image instance so we can use its Save method:
            using (var indexedImageSavingQuantizer = new IndexedImageSavingQuantizer(indexedImage))
            using (var dummyImage = new Image<Rgba32>(indexedImage.Width, indexedImage.Height))
            {
                var imageEncoder = GetIndexedImageEncoder(format, indexedImageSavingQuantizer);
                dummyImage.Save(stream, imageEncoder);
            }
        }

        private static QuantizingImageEncoder GetIndexedImageEncoder(ImageFormat format, IQuantizer quantizer)
        {
            switch (format)
            {
                case ImageFormat.Png:
                    return new PngEncoder
                    {
                        BitDepth = PngBitDepth.Bit8,
                        ColorType = PngColorType.Palette,
                        Quantizer = quantizer,
                    };

                case ImageFormat.Gif:
                    return new GifEncoder
                    {
                        ColorTableMode = GifColorTableMode.Global,
                        Quantizer = quantizer,
                    };

                case ImageFormat.Bmp:
                    return new BmpEncoder
                    {
                        BitsPerPixel = BmpBitsPerPixel.Pixel8,
                        Quantizer = quantizer,
                    };

                default: throw new NotSupportedException($"{format} format does not support indexed images.");
            }
        }


        /// <summary>
        /// This sets the biClrUsed field of the bitmap info header to the number of colors used.
        /// ImageSharp sets that field to 0, which means that the maximum number of colors for the given bit-depth is used,
        /// but somehow Sven Co-op doesn't interpret that value correctly because it breaks color remapping for multiplayer model portraits.
        /// </summary>
        private static void SetBitmapColorsUsedField(Stream stream, int colorsCount)
        {
            stream.Position = 0;
            var fileSignature = stream.ReadString(2);
            if (fileSignature != "BM")
                return;


            stream.Position = 46;
            var colorsUsed = stream.ReadInt();

            if (colorsUsed == 0)
            {
                stream.Position = 28;
                var bitsPerPixel = stream.ReadUint();
                var maxColors = 1 << (int)bitsPerPixel;

                colorsUsed = Math.Clamp(colorsCount, 1, maxColors);

                stream.Position = 46;
                stream.Write(colorsUsed);
            }
        }
    }
}
