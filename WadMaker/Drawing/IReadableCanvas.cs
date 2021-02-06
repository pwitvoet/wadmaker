using System.Drawing;
using System.Drawing.Imaging;

namespace WadMaker.Drawing
{
    /// <summary>
    /// A 2-dimensional grid of pixels whose colors can be read.
    /// </summary>
    public interface IReadableCanvas
    {
        int Width { get; }
        int Height { get; }
        PixelFormat PixelFormat { get; }

        /// <summary>
        /// Returns the color of the pixel at the given coordinates.
        /// </summary>
        Color GetPixel(int x, int y);

        /// <summary>
        /// Copies the contents of this canvas to another canvas.
        /// </summary>
        void CopyTo(ICanvas destination);
    }
}
