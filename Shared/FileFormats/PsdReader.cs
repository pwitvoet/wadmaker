using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shared.FileFormats
{
    // TODO: I've only been able to test RGB and Grayscale so far, not Bitmap and Indexed!
    // TODO: I also haven't found any files that us the zip/zip-with-prediction compression methods.

    /// <summary>
    /// An image reader for Photoshop (.psd, .psb) files.
    /// Works by reading the embedded composite image, which is only available in files that have been saved with 'maximize compatibility' enabled.
    /// </summary>
    public class PsdReader : IImageReader
    {
        public string[] SupportedExtensions => new[] { "psd", "psb" };


        public Image<Rgba32> ReadImage(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ExtractCompositeImage(file);
        }


        private static Image<Rgba32> ExtractCompositeImage(Stream stream)
        {
            // Header:
            var signature = stream.ReadString(4);
            if (signature != "8BPS") throw new InvalidDataException($"Invalid file signature: '{signature}'.");

            var version = ReadShortBigEndian(stream);
            if (version != 1 && version != 2) throw new NotSupportedException($"Unsupported file version: {version}.");
            var isBigFile = version == 2;

            stream.Seek(6, SeekOrigin.Current); // Reserved bytes

            var channelCount = ReadShortBigEndian(stream);
            var height =ReadIntBigEndian(stream);
            var width = ReadIntBigEndian(stream);
            var depth = ReadShortBigEndian(stream);
            var colorMode = (ColorMode)ReadShortBigEndian(stream);


            // Color-mode data:
            var colorModeDataLength = ReadIntBigEndian(stream);
            var palette = new Rgba32[] { };
            if (colorMode == ColorMode.Indexed)
            {
                var paletteData = stream.ReadBytes(256 * 3);
                palette = Enumerable.Range(0, 256)
                    .Select(i => new Rgba32(paletteData[i], paletteData[256 + i], paletteData[512 + i]))
                    .ToArray();
            }
            else
            {
                stream.Seek(colorModeDataLength, SeekOrigin.Current);
            }


            // TODO: Check color profile information!
            // Image resource blocks:
            var imageResourcesLength = ReadIntBigEndian(stream);
            stream.Seek(imageResourcesLength, SeekOrigin.Current);


            // Layer and mask information:
            var layersAndMasksLength = isBigFile ? ReadLongBigEndian(stream) : ReadIntBigEndian(stream);
            stream.Seek(layersAndMasksLength, SeekOrigin.Current);


            // The composite image is stored at the end, but only if 'maximize compatibility' is enabled:
            if (stream.Position >= stream.Length) throw new InvalidDataException($"No composite image found.");
            return ReadCompositeImage(stream, channelCount, width, height, depth, colorMode, palette, isBigFile);
        }

        private static Image<Rgba32> ReadCompositeImage(Stream stream, short channelCount, int width, int height, short depth, ColorMode colorMode, Rgba32[] palette, bool isBigFile)
        {
            var compressionMethod = (CompressionMethod)ReadShortBigEndian(stream);
            var channelData = ReadChannelData(stream, channelCount, width, height, depth, compressionMethod, isBigFile);
            var getValue = GetValueFunction(depth, channelData);
            var getColor = GetColorFunction(colorMode, channelCount, palette, getValue);

            var image = new Image<Rgba32>(width, height);
            for (int y = 0; y < height; y++)
            {
                var rowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    rowSpan[x] = getColor(x, y);
                }
            }
            return image;
        }

        // Result format: channelData[channel][y][..x..]
        private static byte[][][] ReadChannelData(Stream stream, short channelCount, int width, int height, short depth, CompressionMethod compressionMethod, bool isBigFile)
        {
            switch (compressionMethod)
            {
                case CompressionMethod.Raw:
                {
                    var scanlineLength = ScanlineLength(depth, width);
                    return Enumerable.Range(0, channelCount)
                        .Select(channel => Enumerable.Range(0, height)
                            .Select(row => stream.ReadBytes(scanlineLength))
                            .ToArray())
                        .ToArray();
                }

                case CompressionMethod.RunLengthEncoded:
                {
                    var scanlineLengths = Enumerable.Range(0, channelCount)
                        .Select(channel => Enumerable.Range(0, height)
                            .Select(row => isBigFile ? ReadIntBigEndian(stream) : ReadShortBigEndian(stream))
                            .ToArray())
                        .ToArray();

                    return Enumerable.Range(0, channelCount)
                        .Select(channel => Enumerable.Range(0, height)
                            .Select(row => UnpackBits(stream.ReadBytes(scanlineLengths[channel][row])))
                            .ToArray())
                        .ToArray();
                }

                // TODO: Implement this!
                case CompressionMethod.Zip:
                case CompressionMethod.ZipWithPrediction:
                    throw new NotSupportedException($"Compression method {compressionMethod} is currently not supported.");

                default:
                    throw new InvalidDataException($"Invalid image data compression method: {compressionMethod}.");
            }
        }


        private static int ScanlineLength(short depth, int width)
        {
            switch (depth)
            {
                case 1: return (width + 7) / 8;
                case 8: return width;
                case 16: return width * 2;
                case 32: return width * 4;
                default: throw new InvalidDataException($"Invalid channel depth: {depth}.");
            }
        }

        private static byte[] UnpackBits(byte[] packed)
        {
            var unpacked = new List<byte>(packed.Length);
            for (int i = 0; i < packed.Length; i++)
            {
                var packedByte = (int)packed[i];
                i += 1;
                if (packedByte >= 128)
                {
                    for (int j = 256 - packedByte; j >= 0; j--)
                        unpacked.Add(packed[i]);
                }
                else
                {
                    for (int j = 0; j <= packedByte; j++)
                        unpacked.Add(packed[i + j]);
                    i += packedByte;
                }
            }
            return unpacked.ToArray();
        }

        private static Func<int, int, int, int> GetValueFunction(short depth, byte[][][] channelData)
        {
            switch (depth)
            {
                case 1:
                    return (channel, x, y) => (channelData[channel][y][x / 8] >> (7 - (x & 3))) & 1;

                case 8:
                    return (channel, x, y) => channelData[channel][y][x];

                case 16:
                    return (channel, x, y) => channelData[channel][y][x * 2];   // Just taking the most significant byte (big-endian int16)

                case 32:
                    return (channel, x, y) =>
                    {
                        // Big-endian floating point:
                        var row = channelData[channel][y];
                        var data = new byte[] { row[x * 4 + 3], row[x * 4 + 2], row[x * 4 + 1], row[x * 4] };
                        return (int)(BitConverter.ToSingle(data, 0) * 255);
                    };

                default:
                    throw new InvalidDataException($"Invalid channel depth: {depth}.");
            }
        }

        private static Func<int, int, Rgba32> GetColorFunction(ColorMode colorMode, short channelCount, Rgba32[] palette, Func<int, int, int, int> getValue)
        {
            switch (colorMode)
            {
                case ColorMode.Bitmap:
                    return (x, y) => (getValue(0, x, y) == 0) ? new Rgba32(255, 255, 255) : new Rgba32(0, 0, 0);

                case ColorMode.Grayscale:
                    return (x, y) =>
                    {
                        var value = (byte)getValue(0, x, y);
                        return new Rgba32(value, value, value);
                    };

                case ColorMode.Indexed:
                    return (x, y) => palette[getValue(0, x, y)];

                case ColorMode.RGB:
                    if (channelCount > 3)
                    {
                        return (x, y) =>
                        {
                            return new Rgba32(
                                (byte)getValue(0, x, y),
                                (byte)getValue(1, x, y),
                                (byte)getValue(2, x, y),
                                (byte)getValue(3, x, y));
                        };
                    }
                    else
                    {
                        return (x, y) =>
                        {
                            return new Rgba32(
                                (byte)getValue(0, x, y),
                                (byte)getValue(1, x, y),
                                (byte)getValue(2, x, y));
                        };
                    }

                default:
                case ColorMode.CMYK:
                case ColorMode.MultiChannel:
                case ColorMode.Duotone:
                case ColorMode.LAB:
                    throw new NotSupportedException($"Color mode {colorMode} is not supported.");
            }
        }


        private static int ReadIntBigEndian(Stream stream)
        {
            var data = stream.ReadBytes(4);
            return (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        }

        private static short ReadShortBigEndian(Stream stream)
        {
            var data = stream.ReadBytes(2);
            return (short)((data[0] << 8) | data[1]);
        }

        private static long ReadLongBigEndian(Stream stream)
        {
            var data = stream.ReadBytes(8);
            return ((long)data[0] << 56) | ((long)data[1] << 48) | ((long)data[2] << 40) | ((long)data[3] << 32) | ((long)data[4] << 24) | ((long)data[5] << 16) | ((long)data[6] << 8) | (long)data[7];
        }


        enum ColorMode : short
        {
            Bitmap = 0,         // 1bpp black/white
            Grayscale = 1,      // 1 grayscale channel
            Indexed = 2,        // 1 channel that indexes into a 256-color palette
            RGB = 3,            // 3 or 4 channels (RGB or RGBA)

            // Not supported:
            CMYK = 4,
            MultiChannel = 7,
            Duotone = 8,
            LAB = 9,
        }

        enum CompressionMethod : short
        {
            Raw = 0,
            RunLengthEncoded = 1,
            Zip = 2,
            ZipWithPrediction = 3,
        }
    }
}
