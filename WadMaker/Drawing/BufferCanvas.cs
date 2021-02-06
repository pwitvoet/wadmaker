using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    public abstract class BufferCanvas : IBufferCanvas
    {
        public int Width { get; }
        public int Height { get; }
        public abstract PixelFormat PixelFormat { get; }

        int IBufferCanvas.Stride => Stride;
        byte[] IBufferCanvas.Buffer => Buffer;


        protected int Stride { get; }
        protected byte[] Buffer { get; }


        protected BufferCanvas(int width, int height, int stride, byte[] buffer)
        {
            Width = width;
            Height = height;
            Stride = stride;
            Buffer = buffer;
        }


        public abstract Color GetPixel(int x, int y);

        public virtual void CopyTo(ICanvas destination)
        {
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
                        destination.SetPixel(x, y, GetPixel(x, y));
            }
        }
    }
}
