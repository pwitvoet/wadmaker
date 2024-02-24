using SixLabors.ImageSharp.PixelFormats;

namespace Shared
{
    public static class Bsp
    {
        /// <summary>
        /// Reads embedded textures from the specified bsp file.
        /// </summary>
        public static List<Texture> GetEmbeddedTextures(string path)
        {
            using (var file = File.OpenRead(path))
                return GetEmbeddedTextures(file);
        }

        public static List<Texture> GetEmbeddedTextures(Stream stream)
        {
            var bspHeader = ReadBspHeader(stream);
            var textureLumpOffset = bspHeader.Lumps[TexturesLumpIndex].Offset;

            stream.Seek(textureLumpOffset, SeekOrigin.Begin);
            var textureOffsets = ReadTextureOffsets(stream);

            var textures = new List<Texture>();
            for (int i = 0; i < textureOffsets.Length; i++)
            {
                stream.Seek(textureLumpOffset + textureOffsets[i], SeekOrigin.Begin);
                var texture = ReadTexture(stream);
                if (texture.ImageData == null)
                    continue;

                textures.Add(Texture.CreateMipmapTexture(
                    texture.Name,
                    (int)texture.Width,
                    (int)texture.Height,
                    texture.ImageData[0],
                    texture.Palette,
                    texture.ImageData[1],
                    texture.ImageData[2],
                    texture.ImageData[3]));
            }
            return textures;
        }

        /// <summary>
        /// Removes embedded textures from the specified bsp file.
        /// If no output path is provided, then the input bsp file is overwritten.
        /// </summary>
        public static int RemoveEmbeddedTextures(string path, string outputPath = null)
        {
            // Read all bsp lump contents:
            var lumps = new List<Lump>();
            var lumpsData = new List<byte[]>();
            using (var inputStream = File.OpenRead(path))
            {
                var bspHeader = ReadBspHeader(inputStream);
                lumps.AddRange(bspHeader.Lumps);
                lumpsData.AddRange(bspHeader.Lumps.Select(lump => ReadLumpData(inputStream, lump)));
            }

            // Remove image and palette data from embedded textures:
            var removedTextureCount = 0;
            BspTexture[] bspTextures;
            using (var texturesLumpstream = new MemoryStream(lumpsData[TexturesLumpIndex]))
            {
                var textureOffsets = ReadTextureOffsets(texturesLumpstream);
                bspTextures = new BspTexture[textureOffsets.Length];

                for (int i = 0; i < textureOffsets.Length; i++)
                {
                    texturesLumpstream.Seek(textureOffsets[i], SeekOrigin.Begin);
                    var bspTexture = ReadTexture(texturesLumpstream);
                    if (bspTexture.IsEmbedded)
                    {
                        bspTexture.ImageData = null;
                        bspTexture.Palette = null;
                        removedTextureCount += 1;
                    }
                    bspTextures[i] = bspTexture;
                }
            }

            // Create new texture lump data:
            using (var stream = new MemoryStream())
            {
                WriteTextureOffsets(stream, bspTextures);
                foreach (var bspTexture in bspTextures)
                    WriteTexture(stream, bspTexture);

                lumpsData[TexturesLumpIndex] = stream.ToArray();
            }

            // Recalculate lump offsets/lengths:
            var lumpIndexes = lumps
                .Select((lump, i) => (lump, i))
                .OrderBy(pair => pair.lump.Offset)
                .Select(pair => pair.i)
                .ToArray();
            var offset = 4 + lumps.Count * 8;   // 124, BSP header size (15 lumps)
            for (int i = 0; i < lumpIndexes.Length; i++)
            {
                var lumpIndex = lumpIndexes[i];
                lumps[lumpIndex] = new Lump { Offset = offset, Length = lumpsData[lumpIndex].Length };
                offset += lumpsData[lumpIndex].Length;
                offset += StreamExtensions.RequiredPadding(lumpsData[lumpIndex].Length, 4);
            }

            // Save the bsp file:
            using (var outputStream = File.OpenWrite(outputPath ?? path))
            {
                WriteBspHeader(outputStream, new BspHeader { Version = 30, Lumps = lumps.ToArray() });
                foreach (var lumpIndex in lumpIndexes)
                    WriteLumpData(outputStream, lumpsData[lumpIndex]);
            }

            return removedTextureCount;
        }


        private static BspHeader ReadBspHeader(Stream stream)
        {
            var header = new BspHeader();
            header.Version = stream.ReadInt();
            if (header.Version != 30)
                throw new NotSupportedException("Only BSP v30 is supported.");

            header.Lumps = Enumerable.Range(0, 15)
                .Select(i => new Lump { Offset = stream.ReadInt(), Length = stream.ReadInt() })
                .ToArray();

            return header;
        }

        private static byte[] ReadLumpData(Stream stream, Lump lump)
        {
            stream.Seek(lump.Offset, SeekOrigin.Begin);
            return stream.ReadBytes(lump.Length);
        }

        private static int[] ReadTextureOffsets(Stream stream)
        {
            var textureCount = stream.ReadInt();
            return Enumerable.Range(0, textureCount)
                .Select(i => stream.ReadInt())
                .ToArray();
        }

        private static BspTexture ReadTexture(Stream stream)
        {
            var textureOffset = stream.Position;

            var texture = new BspTexture();
            texture.Name = stream.ReadString(16);
            texture.Width = stream.ReadUint();
            texture.Height = stream.ReadUint();

            var imageDataOffsets = Enumerable.Range(0, 4)
                .Select(i => stream.ReadUint())
                .ToArray();

            // Is this an embedded texture?
            if (imageDataOffsets[0] == 0)
                return texture;

            texture.ImageData = new byte[4][];
            for (int j = 0; j < 4; j++)
            {
                if (imageDataOffsets[j] == 0)
                    continue;

                stream.Seek(textureOffset + imageDataOffsets[j], SeekOrigin.Begin);
                texture.ImageData[j] = stream.ReadBytes((int)(texture.Width * texture.Height) >> (j * 2));
            }
            var paletteSize = stream.ReadUshort();
            texture.Palette = Enumerable.Range(0, paletteSize)
                .Select(i => stream.ReadColor())
                .ToArray();

            return texture;
        }


        private static void WriteBspHeader(Stream stream, BspHeader header)
        {
            stream.Write(header.Version);

            foreach (var lump in header.Lumps)
            {
                stream.Write(lump.Offset);
                stream.Write(lump.Length);
            }
        }

        private static void WriteLumpData(Stream stream, byte[] lumpData)
        {
            stream.Write(lumpData);

            var padding = StreamExtensions.RequiredPadding(lumpData.Length, 4);
            if (padding > 0)
                stream.Write(new byte[padding]);
        }

        private static void WriteTextureOffsets(Stream stream, BspTexture[] textures)
        {
            stream.Write(textures.Length);
            var offset = 4 + textures.Length * 4;
            foreach (var texture in textures)
            {
                stream.Write(offset);
                if (texture.IsEmbedded)
                {
                    offset += 40 + texture.ImageData.Sum(imageData => imageData.Length) + 2 + texture.Palette.Length * 3;
                    offset += StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4);
                }
                else
                {
                    offset += 40;
                }
            }
        }

        private static void WriteTexture(Stream stream, BspTexture texture)
        {
            stream.Write(texture.Name, 16);
            stream.Write(texture.Width);
            stream.Write(texture.Height);

            if (!texture.IsEmbedded)
            {
                for (int i = 0; i < 4; i++)
                    stream.Write(0u);
                return;
            }
            else
            {
                var offset = 40u;
                foreach (var imageData in texture.ImageData)
                {
                    stream.Write(offset);
                    offset += (uint)imageData.Length;
                }

                foreach (var imageData in texture.ImageData)
                {
                    if (imageData != null)
                        stream.Write(imageData);
                }

                stream.Write((ushort)texture.Palette.Length);
                foreach (var color in texture.Palette)
                    stream.Write(color);
                stream.Write(new byte[StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4)]);
            }
        }


        private const int TexturesLumpIndex = 2;


        struct BspHeader
        {
            public int Version;
            public Lump[] Lumps;
        }

        struct Lump
        {
            public int Offset;
            public int Length;
        }

        struct BspTexture
        {
            public string Name;
            public uint Width;
            public uint Height;
            public byte[][] ImageData;
            public Rgba32[] Palette;

            public bool IsEmbedded => ImageData != null && ImageData.All(data => data != null) && Palette != null;
        }
    }
}
