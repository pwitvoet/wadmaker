using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace WadMaker.Drawing
{
    abstract class IndexedCanvas : BufferCanvas, IIndexedCanvas
    {
        public static IIndexedCanvas Create(int width, int height, PixelFormat pixelFormat, Color[] palette, byte[] buffer = null, int? stride = null)
        {
            if (width < 1) throw new ArgumentException($"{nameof(width)} must be greater than 0.");
            if (height < 1) throw new ArgumentException($"{nameof(height)} must be greater than 0.");

            var actualStride = stride ?? PixelFormatUtils.GetStride(width, pixelFormat);
            if (actualStride < PixelFormatUtils.GetStride(width, pixelFormat, false)) throw new ArgumentException($"{nameof(stride)} must be greater than {PixelFormatUtils.GetStride(width, pixelFormat, false)}.");
            if (buffer?.Length < actualStride * height) throw new ArgumentException($"{nameof(buffer)} must be at least {actualStride * height} bytes.");
            if (!pixelFormat.HasFlag(PixelFormat.Indexed)) throw new NotSupportedException($"For non-indexed pixel formats, use {nameof(Canvas)}.{nameof(Canvas.Create)}.");

            if (buffer == null)
                buffer = new byte[actualStride * height];

            switch (pixelFormat)
            {
                case PixelFormat.Format1bppIndexed: return new Canvas1bppIndexed(width, height, actualStride, buffer, CreateFixedSizePalette(palette, 2));
                case PixelFormat.Format4bppIndexed: return new Canvas4bppIndexed(width, height, actualStride, buffer, CreateFixedSizePalette(palette, 16));
                case PixelFormat.Format8bppIndexed: return new Canvas8bppIndexed(width, height, actualStride, buffer, CreateFixedSizePalette(palette, 256));

                default: throw new NotSupportedException($"{nameof(IndexedCanvas)} does not support pixel format {pixelFormat}.");
            }
        }

        public static IIndexedCanvas Create(Bitmap bitmap)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try
            {
                var buffer = new byte[bitmapData.Stride * bitmapData.Height];
                Marshal.Copy(bitmapData.Scan0, buffer, 0, buffer.Length);
                return Create(bitmap.Width, bitmap.Height, bitmap.PixelFormat, bitmap.Palette.Entries, buffer, bitmapData.Stride);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }


        public Color[] Palette { get; }


        protected IndexedCanvas(int width, int height, int stride, byte[] buffer, Color[] palette)
            : base(width, height, stride, buffer)
        {
            Palette = palette;
        }


        public override Color GetPixel(int x, int y) => Palette[GetIndex(x, y)];

        public abstract int GetIndex(int x, int y);

        public abstract void SetIndex(int x, int y, int index);

        public void CopyTo(IIndexedCanvas destination)
        {
            if (destination.Palette.Length < Palette.Length) throw new InvalidOperationException($"Destination canvas palette is too small.");


            Array.Copy(Palette, destination.Palette, Palette.Length);

            if (PixelFormat == destination.PixelFormat && destination is IBufferCanvas bufferCanvas)
            {
                if (Stride == bufferCanvas.Stride)
                {
                    Array.Copy(Buffer, bufferCanvas.Buffer, Stride * Math.Min(Height, destination.Height));
                }
                else
                {
                    // TODO: Stride may contain some unused bytes due to alignment - which may cause artifacts along the right side of the copied area, if the destination is wider!
                    var minStride = Math.Min(Stride, bufferCanvas.Stride);
                    var minHeight = Math.Min(Height, destination.Height);

                    for (int y = 0; y < minHeight; y++)
                        Array.Copy(Buffer, y * Stride, bufferCanvas.Buffer, y * bufferCanvas.Stride, minStride);
                }
            }
            else
            {
                var minWidth = Math.Min(Width, destination.Width);
                var minHeight = Math.Min(Height, destination.Height);

                for (int y = 0; y < minHeight; y++)
                    for (int x = 0; x < minWidth; x++)
                        destination.SetIndex(x, y, GetIndex(x, y));
            }
        }


        private static Color[] CreateFixedSizePalette(Color[] palette, int size)
        {
            if (palette.Length > size)
                throw new ArgumentException($"Palette must not contain more than {size} colors.", nameof(palette));

            if (palette.Length == size)
                return palette;

            return palette
                .Concat(Enumerable.Repeat(Color.FromArgb(0, 0, 0), size - palette.Length))
                .ToArray();
        }
    }
}
