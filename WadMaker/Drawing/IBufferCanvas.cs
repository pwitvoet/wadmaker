
namespace WadMaker.Drawing
{
    /// <summary>
    /// A canvas that is backed by a byte array buffer. Enables more efficient copying in certain situations.
    /// </summary>
    public interface IBufferCanvas : IReadableCanvas
    {
        /// <summary>
        /// The number of bytes that each pixel row occupies in the buffer.
        /// </summary>
        int Stride { get; }

        /// <summary>
        /// The buffer that contains the contents of this canvas.
        /// </summary>
        byte[] Buffer { get; }
    }
}
