using System.Drawing;

namespace WadMaker.Drawing
{
    /// <summary>
    /// A 2-dimensional grid of pixels whose colors can both be read and modified.
    /// </summary>
    public interface ICanvas : IReadableCanvas
    {
        /// <summary>
        /// Sets the color of the pixel at the given coordinates.
        /// For canvases with an indexed pixel format, use <see cref="SetIndex(int, int, int)"/> instead.
        /// </summary>
        void SetPixel(int x, int y, Color color);
    }
}
