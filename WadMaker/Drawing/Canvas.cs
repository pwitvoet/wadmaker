using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WadMaker.Drawing
{
    public abstract class Canvas : BufferCanvas, ICanvas
    {
        public static ICanvas Create(int width, int height, PixelFormat pixelFormat, byte[] buffer = null, int? stride = null)
        {
            if (width < 1) throw new ArgumentException($"{nameof(width)} must be greater than 0.");
            if (height < 1) throw new ArgumentException($"{nameof(height)} must be greater than 0.");

            var actualStride = stride ?? PixelFormatUtils.GetStride(width, pixelFormat);
            if (actualStride < PixelFormatUtils.GetStride(width, pixelFormat, false)) throw new ArgumentException($"{nameof(stride)} must be greater than {PixelFormatUtils.GetStride(width, pixelFormat, false)}.");
            if (buffer?.Length < actualStride * height) throw new ArgumentException($"{nameof(buffer)} must be at least {actualStride * height} bytes.");
            if (pixelFormat.HasFlag(PixelFormat.Indexed)) throw new NotSupportedException($"For indexed pixel formats, use {nameof(IndexedCanvas)}.{nameof(IndexedCanvas.Create)}.");

            if (buffer == null)
                buffer = new byte[actualStride * height];

            switch (pixelFormat)
            {
                case PixelFormat.Format16bppRgb555: return new Canvas16bppRgb555(width, height, actualStride, buffer);
                case PixelFormat.Format16bppRgb565: return new Canvas16bppRgb565(width, height, actualStride, buffer);
                case PixelFormat.Format16bppArgb1555: return new Canvas16bppArgb1555(width, height, actualStride, buffer);

                case PixelFormat.Format24bppRgb: return new Canvas24bppRgb(width, height, actualStride, buffer);

                case PixelFormat.Format32bppRgb: return new Canvas32bppRgb(width, height, actualStride, buffer);
                case PixelFormat.Format32bppArgb: return new Canvas32bppArgb(width, height, actualStride, buffer);

                default: throw new NotSupportedException($"{nameof(Canvas)} does not support pixel format {pixelFormat}.");
            }
        }

        public static ICanvas Create(Bitmap bitmap)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try
            {
                var buffer = new byte[bitmapData.Stride * bitmapData.Height];
                Marshal.Copy(bitmapData.Scan0, buffer, 0, buffer.Length);
                return Create(bitmap.Width, bitmap.Height, bitmap.PixelFormat, buffer, bitmapData.Stride);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }


        protected Canvas(int width, int height, int stride, byte[] buffer)
            : base(width, height, stride, buffer)
        {
        }

        public abstract void SetPixel(int x, int y, ColorARGB color);
    }
}
