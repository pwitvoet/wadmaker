using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WadMaker.FileFormats
{
    /// <summary>
    /// An image reader can read or extract images from files.
    /// </summary>
    interface IImageReader
    {
        /// <summary>
        /// The list of file extensions that this image reader supports (excluding leading dots).
        /// </summary>
        string[] SupportedExtensions { get; }


        /// <summary>
        /// Reads or extracts an image from the given file.
        /// </summary>
        Image<Rgba32> ReadImage(string path);
    }
}
