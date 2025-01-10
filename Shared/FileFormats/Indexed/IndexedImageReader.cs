using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;
using SixLabors.ImageSharp;

namespace Shared.FileFormats.Indexed
{
    internal class IndexedImageReader
    {
        public static bool IsIndexed(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var format = Image.DetectFormat(file);

                switch (format)
                {
                    case PngFormat: return IsPngIndexed(file);
                    case GifFormat: return IsGifIndexed(file);
                    case BmpFormat: return IsBmpIndexed(file);
                    default: return false;
                }
            }
        }

        public static IndexedImage LoadIndexedImage(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var format = Image.DetectFormat(file);
                switch (format)
                {
                    case PngFormat: return ReadIndexedPng(file);
                    case GifFormat: return ReadIndexedGif(file);
                    case BmpFormat: return ReadIndexedBmp(file);
                    default: throw new InvalidDataException($"{path} is not an indexed image.");
                }
            }
        }


        private static bool IsPngIndexed(Stream stream)
        {
            var position = stream.Position;
            try
            {
                stream.Position += 8;
                while (stream.Position < stream.Length)
                {
                    var chunkSize = stream.ReadIntBigEndian();
                    var chunkName = stream.ReadString(4);

                    // Search for the header chunk:
                    if (chunkName == "IHDR")
                    {
                        stream.Position += 9;

                        // Check if the color type has the indexed (1) and color (2) bits set:
                        var colorType = stream.ReadByte();
                        return colorType == 3;
                    }
                    else
                    {
                        stream.Position += chunkSize + 4;
                        continue;
                    }
                }

                return false;
            }
            finally
            {
                stream.Position = position;
            }
        }

        private static bool IsGifIndexed(Stream stream)
        {
            var position = stream.Position;
            try
            {
                stream.Position += 10;
                var info = stream.ReadByte();
                if (info == -1)
                    return false;

                // Check if the global palette bit is set:
                return (info & 0x80) != 0;
            }
            finally
            {
                stream.Position = position;
            }
        }

        private static bool IsBmpIndexed(Stream stream)
        {
            var position = stream.Position;
            try
            {
                stream.Position += 28;

                // Check if the number of bits per pixel indicates an indexed bitmap:
                var bitsPerPixel = stream.ReadUshort();
                return bitsPerPixel == 1 || bitsPerPixel == 4 || bitsPerPixel == 8;
            }
            finally
            {
                stream.Position = position;
            }
        }


        private static IndexedImage ReadIndexedPng(Stream stream)
        {
            if (!IsPngIndexed(stream))
                throw new InvalidDataException("Image is not an indexed png.");


            // Find the palette and its position in the file:
            var paletteOffset = 0;
            var palette = Array.Empty<Rgba32>();

            stream.Position += 8;
            while (stream.Position < stream.Length)
            {
                var chunkSize = stream.ReadIntBigEndian();
                var chunkName = stream.ReadString(4);

                // Search for the palette chunk:
                if (chunkName == "PLTE")
                {
                    paletteOffset = (int)stream.Position;
                    var paletteData = stream.ReadBytes(chunkSize);

                    var colorCount = chunkSize / 3;
                    palette = new Rgba32[colorCount];
                    for (int i = 0; i < colorCount; i++)
                        palette[i] = new Rgba32(paletteData[i * 3], paletteData[i * 3 + 1], paletteData[i * 3 + 2]);

                    break;
                }
                else
                {
                    // Skip other chunks and their CRC:
                    stream.Position += chunkSize + 4;
                }
            }

            if (palette.Length == 0)
                throw new InvalidDataException("Palette not found.");


            // Create a fake palette, where the R value of each color matches the position of that color in the palette:
            var fakePaletteData = new byte[palette.Length * 3];
            for (int i = 0; i < palette.Length; i++)
                fakePaletteData[i * 3] = (byte)i;

            var fakePaletteDataCrc = BitConverter.GetBytes(GetCrc32(Encoding.ASCII.GetBytes("PLTE").Concat(fakePaletteData)));
            Array.Reverse(fakePaletteDataCrc);      // PNG stores CRCs in big endian order.
            fakePaletteData = fakePaletteData.Concat(fakePaletteDataCrc).ToArray();


            return ReadIndexedImage(PngDecoder.Instance, stream, palette, (paletteOffset, fakePaletteData));
        }

        private static IndexedImage ReadIndexedGif(Stream stream)
        {
            if (!IsGifIndexed(stream))
                throw new InvalidDataException("Image is not an indexed gif.");


            // Find the palette:
            stream.Position += 10;
            var info = stream.ReadByte();
            var bitPerPixel = (info & 0x07) + 1;
            var colorCount = 1 << bitPerPixel;

            stream.Position += 2;
            var paletteData = stream.ReadBytes(colorCount * 3);
            var palette = new Rgba32[colorCount];
            for (int i = 0; i < colorCount; i++)
                palette[i] = new Rgba32(paletteData[i * 3], paletteData[i * 3 + 1], paletteData[i * 3 + 2]);


            // Create a fake palette, where the R value of each color matches the position of that color in the palette:
            var fakePaletteData = new byte[palette.Length * 3];
            for (int i = 0; i < palette.Length; i++)
                fakePaletteData[i * 3] = (byte)i;


            return ReadIndexedImage(GifDecoder.Instance, stream, palette, (13, fakePaletteData));
        }

        private static IndexedImage ReadIndexedBmp(Stream stream)
        {
            if (!IsBmpIndexed(stream))
                throw new InvalidDataException("Image is not an indexed bmp.");

            var position = stream.Position;
            try
            {
                // Find the palette:
                stream.Position += 28;
                var bitsPerPixel = stream.ReadUshort();
                var colorCount = 1 << bitsPerPixel;

                stream.Position += 24;
                var paletteData = stream.ReadBytes(colorCount * 4);
                var palette = new Rgba32[colorCount];
                for (int i = 0; i < colorCount; i++)
                    palette[i] = new Rgba32(paletteData[i * 4 + 2], paletteData[i * 4 + 1], paletteData[i * 4]);


                // Create a fake palette, where the R value of each color matches the position of that color in the palette:
                var fakePaletteData = new byte[palette.Length * 4];
                for (int i = 0; i < palette.Length; i++)
                    fakePaletteData[i * 4 + 2] = (byte)i;


                return ReadIndexedImage(BmpDecoder.Instance, stream, palette, (54, fakePaletteData));
            }
            finally
            {
                stream.Position = position;
            }
        }

        /// <summary>
        /// Reads an indexed image from the given stream.
        /// A custom palette, where the R value of each color matches the index of that color in the palette,
        /// should be provided as <paramref name="fakePalettePatches"/>, so the actual index data can be reconstructed.
        /// </summary>
        private static IndexedImage ReadIndexedImage(IImageDecoder decoder, Stream stream, Rgba32[] originalPalette, params (int, byte[])[] fakePalettePatches)
        {
            stream.Position = 0;
            using (var patchedStream = new PatchingStream(stream, true))
            {
                foreach ((var offset, var data) in fakePalettePatches)
                    patchedStream.AddPatch(offset, data);

                using (var image = decoder.Decode<Rgba32>(new DecoderOptions(), patchedStream))
                {
                    var indexedImage = new IndexedImage(image.Width, image.Height, originalPalette);

                    foreach (var frame in image.Frames)
                    {
                        // With our fake palette, the red channel now gives us the original palette index of each pixel:
                        var imageData = new byte[frame.Width * frame.Height];
                        for (int y = 0; y < frame.Height; y++)
                            for (int x = 0; x < frame.Width; x++)
                                imageData[y * frame.Width + x] = frame[x, y].R;

                        indexedImage.Frames.Add(new IndexedImageFrame(imageData, frame.Width, frame.Height));
                    }

                    return indexedImage;
                }
            }
        }


        private static uint[]? _crc32LookupTable;

        private static uint[] GetCrc32LookupTable()
        {
            if (_crc32LookupTable is null)
            {
                _crc32LookupTable = new uint[256];
                for (int i = 0; i < _crc32LookupTable.Length; i++)
                {
                    var c = (uint)i;
                    for (int j = 0; j < 8; j++)
                    {
                        if ((c & 1) == 0)
                            c >>= 1;
                        else
                            c = 0xEDB88320 ^ (c >> 1);
                    }
                    _crc32LookupTable[i] = c;
                }
            }
            return _crc32LookupTable;
        }

        private static uint GetCrc32(IEnumerable<byte> data)
        {
            var table = GetCrc32LookupTable();

            var crc = 0xFFFFFFFF;
            foreach (var b in data)
                crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFF;
        }
    }
}
