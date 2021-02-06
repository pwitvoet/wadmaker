using System.Drawing;

namespace WadMaker.Drawing
{
    /// <summary>
    /// A canvas that uses palette indices rather than colors directly.
    /// </summary>
    public interface IIndexedCanvas : IReadableCanvas
    {
        /// <summary>
        /// A fixed number of colors. Only used for indexed pixel formats.
        /// </summary>
        Color[] Palette { get; }

        /// <summary>
        /// Gets the palette index of the pixel at the given coordinates.
        /// Only supported for indexed pixel formats.
        /// </summary>
        int GetIndex(int x, int y);

        /// <summary>
        /// Sets the palette index of the pixel at the given coordinates.
        /// Only supported for indexed pixel formats.
        /// </summary>
        void SetIndex(int x, int y, int index);


        /// <summary>
        /// Copies the contents of this indexed canvas to another indexed canvas.
        /// This will also overwrite the palette of the destination canvas.
        /// The destination canvas must have an equal or larger palette size.
        /// </summary>
        void CopyTo(IIndexedCanvas destination);
    }
}
