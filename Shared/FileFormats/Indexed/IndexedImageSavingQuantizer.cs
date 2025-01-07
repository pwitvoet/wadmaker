using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using System.Reflection;

namespace Shared.FileFormats.Indexed
{
    /// <summary>
    /// A 'quantizer' that produces an IndexedImageFrame from a specific palette and image data.
    /// This makes it possible to accurately save image data in various formats.
    /// </summary>
    internal class IndexedImageSavingQuantizer : IQuantizer, IQuantizer<Rgba32>
    {
        public Configuration Configuration { get; } = Configuration.Default;
        public QuantizerOptions Options { get; } = new();
        public ReadOnlyMemory<Rgba32> Palette => IndexedImage.Palette;

        private IndexedImage IndexedImage { get; }


        public IndexedImageSavingQuantizer(IndexedImage indexedImage)
        {
            IndexedImage = indexedImage;
        }

        public void Dispose() { }


        // IQuantizer:
        public IQuantizer<TPixel> CreatePixelSpecificQuantizer<TPixel>(Configuration configuration) where TPixel : unmanaged, IPixel<TPixel>
            => CreatePixelSpecificQuantizer<TPixel>(configuration, Options);

        public IQuantizer<TPixel> CreatePixelSpecificQuantizer<TPixel>(Configuration configuration, QuantizerOptions options) where TPixel : unmanaged, IPixel<TPixel>
        {
            if (typeof(TPixel) != typeof(Rgba32))
                throw new InvalidOperationException($"{nameof(IndexedImageSavingQuantizer)} cannot be used with {typeof(TPixel).Name}.");

            return (IQuantizer<TPixel>)this;
        }


        // IQuantizer<Rgba32>:
        public void AddPaletteColors(Buffer2DRegion<Rgba32> pixelRegion) { }    // We already have a palette.

        public IndexedImageFrame<Rgba32> QuantizeFrame(ImageFrame<Rgba32> source, Rectangle bounds)
        {
            // TODO: Unfortunately, IndexedImageFrame<Rgba32> does not have a public contructor, so this may silently break in a future ImageSharp update!
            var indexedImageFrame = (IndexedImageFrame<Rgba32>?)Activator.CreateInstance(
                typeof(IndexedImageFrame<Rgba32>),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new object?[] { Configuration, IndexedImage.Width, IndexedImage.Height, Palette },
                null);
            if (indexedImageFrame is null)
                throw new InvalidOperationException($"Failed to create an {nameof(IndexedImageFrame<Rgba32>)} instance.");

            for (int y = 0; y < IndexedImage.Height; y++)
            {
                var row = indexedImageFrame.GetWritablePixelRowSpanUnsafe(y);
                for (int x = 0; x < IndexedImage.Width; x++)
                    row[x] = IndexedImage[x, y];
            }

            return indexedImageFrame;
        }

        public byte GetQuantizedColor(Rgba32 color, out Rgba32 match)
        {
            // We already have a palette so we're not using this:
            match = color;
            return 0;
        }
    }
}
