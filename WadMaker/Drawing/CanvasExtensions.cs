using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace WadMaker.Drawing
{
    public static class CanvasExtensions
    {
        /// <summary>
        /// Creates a new bitmap from the contents of this canvas, using the same pixel format.
        /// </summary>
        public static Bitmap CreateBitmap(this IReadableCanvas canvas)
        {
            var bitmap = new Bitmap(canvas.Width, canvas.Height, canvas.PixelFormat);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                var buffer = (canvas as IBufferCanvas)?.Buffer ?? canvas.CreateBuffer();
                Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
            return bitmap;
        }

        /// <summary>
        /// Creates a new buffer that contains the contents of this canvas, using the same pixel format.
        /// </summary>
        public static byte[] CreateBuffer(this IReadableCanvas canvas)
        {
            if (canvas is IBufferCanvas bufferCanvas)
                return bufferCanvas.Buffer.ToArray();

            var stride = PixelFormatUtils.GetStride(canvas.Width, canvas.PixelFormat);
            var buffer = new byte[stride * canvas.Height];
            if (canvas is IIndexedCanvas indexedCanvas)
            {
                var targetCanvas = IndexedCanvas.Create(canvas.Width, canvas.Height, canvas.PixelFormat, indexedCanvas.Palette, buffer, stride);
                for (int y = 0; y < canvas.Height; y++)
                    for (int x = 0; x < canvas.Width; x++)
                        targetCanvas.SetIndex(x, y, indexedCanvas.GetIndex(x, y));
                return buffer;
            }
            else
            {
                var targetCanvas = Canvas.Create(canvas.Width, canvas.Height, canvas.PixelFormat, buffer, stride);
                canvas.CopyTo(targetCanvas);
                return buffer;
            }
        }


        /// <summary>
        /// Copies the contents of this canvas to a bitmap.
        /// Copying to an indexed bitmap is only possible if the canvas is also indexed, and if the bitmap palette is not smaller than the canvas palette.
        /// </summary>
        public static void CopyTo(this IReadableCanvas canvas, Bitmap destination)
        {
            var minWidth = Math.Min(canvas.Width, destination.Width);
            var minHeight = Math.Min(canvas.Height, destination.Height);
            var bitmapData = destination.LockBits(new Rectangle(0, 0, destination.Width, destination.Height), ImageLockMode.WriteOnly, destination.PixelFormat);


            try
            {
                if (canvas.PixelFormat == destination.PixelFormat && canvas is IBufferCanvas bufferCanvas)
                {
                    if (canvas is IIndexedCanvas indexedCanvas)
                        Array.Copy(indexedCanvas.Palette, destination.Palette.Entries, indexedCanvas.Palette.Length);

                    if (bufferCanvas.Stride == bitmapData.Stride)
                    {
                        Marshal.Copy(bufferCanvas.Buffer, 0, bitmapData.Scan0, bufferCanvas.Stride * minHeight);
                    }
                    else
                    {
                        // TODO: Stride may contain some unused bytes due to alignment - which may cause artifacts along the right side of the copied area, if the destination is wider!
                        var minStride = Math.Min(bufferCanvas.Stride, bitmapData.Stride);
                        for (int y = 0; y < minHeight; y++)
                            Marshal.Copy(bufferCanvas.Buffer, y * bufferCanvas.Stride, bitmapData.Scan0 + y * bitmapData.Stride, minStride);
                    }
                }
                else if (destination.PixelFormat.HasFlag(PixelFormat.Indexed))
                {
                    if (!(canvas is IIndexedCanvas indexedCanvas))
                        throw new NotSupportedException($"Cannot copy a non-indexed canvas to an indexed bitmap.");
                    if (PixelFormatUtils.GetPaletteSize(destination.PixelFormat) < PixelFormatUtils.GetPaletteSize(canvas.PixelFormat))
                        throw new NotSupportedException($"Cannot copy an indexed canvas to an indexed bitmap with a smaller palette.");


                    Array.Copy(indexedCanvas.Palette, destination.Palette.Entries, indexedCanvas.Palette.Length);

                    var buffer = new byte[bitmapData.Stride * bitmapData.Height];
                    var destinationCanvas = IndexedCanvas.Create(destination.Width, destination.Height, destination.PixelFormat, destination.Palette.Entries, buffer, bitmapData.Stride);
                    if (!(destinationCanvas is IBufferCanvas))
                        throw new InvalidProgramException($"{nameof(IndexedCanvas)}.{nameof(IndexedCanvas.Create)} must produce an {nameof(IIndexedCanvas)}!");

                    indexedCanvas.CopyTo(destinationCanvas);
                    destinationCanvas.CopyTo(destination);
                }
                else
                {
                    var buffer = new byte[bitmapData.Stride * bitmapData.Height];
                    var destinationCanvas = Canvas.Create(destination.Width, destination.Height, destination.PixelFormat, buffer, bitmapData.Stride);
                    if (!(destinationCanvas is IBufferCanvas))
                        throw new InvalidProgramException($"{nameof(Canvas)}.{nameof(Canvas.Create)} must produce an {nameof(IIndexedCanvas)}!");

                    canvas.CopyTo(destinationCanvas);
                    destinationCanvas.CopyTo(destination);
                }
            }
            finally
            {
                destination.UnlockBits(bitmapData);
            }
        }


        public static Color GetAverageColor(this IReadableCanvas canvas)
        {
            long r = 0;
            long g = 0;
            long b = 0;
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    var color = canvas.GetPixel(x, y);
                    r += color.R;
                    g += color.G;
                    b += color.B;
                }
            }
            var pixelCount = canvas.Width * canvas.Height;
            return Color.FromArgb((int)(r / pixelCount), (int)(g / pixelCount), (int)(b / pixelCount));
        }
    }
}
