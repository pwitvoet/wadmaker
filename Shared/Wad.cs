using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shared
{
    public class Wad
    {
        public List<Texture> Textures { get; } = new List<Texture>();


        public void Save(string path)
        {
            using (var file = File.Create(path))
                Save(file);
        }

        public void Save(Stream stream)
        {
            stream.Write("WAD3");
            stream.Write((uint)Textures.Count);

            var textureOffset = (uint)(stream.Position + 4);
            var lumps = Textures
                .Select(texture =>
                {
                    var offset = textureOffset;
                    var textureFileSize = GetTextureFileSize(texture);
                    textureOffset += textureFileSize;

                    return new Lump {
                        Offset = offset,
                        CompressedLength = textureFileSize,
                        FullLength = textureFileSize,
                        Type = texture.Type,
                        CompressionType = 0,    // Always uncompressed
                        Name = texture.Name,
                    };
                })
                .ToArray();
            var lumpOffset = (uint)(stream.Position + 4 + lumps.Sum(lump => lump.CompressedLength));
            stream.Write(lumpOffset);

            foreach (var texture in Textures)
                WriteTexture(stream, texture);

            foreach (var lump in lumps)
                WriteLump(stream, lump);
        }


        public static Wad Load(string path)
        {
            using (var file = File.OpenRead(path))
                return Load(file);
        }

        public static Wad Load(Stream stream)
        {
            var wad = new Wad();

            var wad3MagicString = stream.ReadString(4);
            if (wad3MagicString != "WAD3")
                throw new InvalidDataException($"Expected file to start with 'WAD3' but found '{wad3MagicString}'.");

            var textureCount = stream.ReadUint();

            var lumpOffset = stream.ReadUint();
            stream.Seek(lumpOffset, SeekOrigin.Begin);
            var lumps = Enumerable.Range(0, (int)textureCount)
                .Select(i => ReadLump(stream))
                .ToArray();

            foreach (var lump in lumps)
                wad.Textures.Add(ReadTexture(stream, lump));

            return wad;
        }


        private static void WriteLump(Stream stream, Lump lump)
        {
            stream.Write(lump.Offset);
            stream.Write(lump.CompressedLength);
            stream.Write(lump.FullLength);

            stream.Write((byte)lump.Type);
            stream.Write(lump.CompressionType);
            stream.Write(new byte[2]);  // Padding.

            stream.Write(lump.Name, 16);
        }

        private static void WriteTexture(Stream stream, Texture texture)
        {
            if (texture.Type == TextureType.MipmapTexture)
            {
                stream.Write(texture.Name, 16);
                stream.Write((uint)texture.Width);
                stream.Write((uint)texture.Height);
                stream.Write((uint)40);
                stream.Write((uint)(40 + texture.ImageData.Length));
                stream.Write((uint)(40 + texture.ImageData.Length + texture.Mipmap1Data.Length));
                stream.Write((uint)(40 + texture.ImageData.Length + texture.Mipmap1Data.Length + texture.Mipmap2Data.Length));

                stream.Write(texture.ImageData);
                stream.Write(texture.Mipmap1Data);
                stream.Write(texture.Mipmap2Data);
                stream.Write(texture.Mipmap3Data);

                stream.Write((ushort)texture.Palette.Length);
                if (texture.Type == TextureType.MipmapTexture)
                {
                    foreach (var color in texture.Palette)
                        stream.Write(color);
                }
                stream.Write(new byte[StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4)]);
            }
            else if (texture.Type == TextureType.Font)
            {
                stream.Write((uint)texture.Width);
                stream.Write((uint)texture.Height);

                stream.Write((uint)texture.RowCount);
                stream.Write((uint)texture.RowHeight);
                foreach (var charInfo in texture.CharInfos)
                {
                    stream.Write((ushort)charInfo.StartOffset);
                    stream.Write((ushort)charInfo.CharWidth);
                }
                stream.Write(texture.ImageData);

                stream.Write((ushort)texture.Palette.Length);
                foreach (var color in texture.Palette)
                    stream.Write(color);
                stream.Write(new byte[StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4)]);
            }
            else if (texture.Type == TextureType.SimpleTexture)
            {
                stream.Write((uint)texture.Width);
                stream.Write((uint)texture.Height);
                stream.Write(texture.ImageData);

                stream.Write((ushort)texture.Palette.Length);
                foreach (var color in texture.Palette)
                    stream.Write(color);
                stream.Write(new byte[StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4)]);
            }
            else
            {
                throw new InvalidDataException($"Unknown texture type: {texture.Type}.");
            }
        }


        private static Lump ReadLump(Stream stream)
        {
            var lump = new Lump();
            lump.Offset = stream.ReadUint();
            lump.CompressedLength = stream.ReadUint();
            lump.FullLength = stream.ReadUint();

            var types = stream.ReadBytes(4);    // 2 type bytes + padding.
            lump.Type = (TextureType)types[0];
            lump.CompressionType = types[1];

            lump.Name = stream.ReadString(16);
            return lump;
        }

        private static Texture ReadTexture(Stream stream, Lump lump)
        {
            stream.Seek(lump.Offset, SeekOrigin.Begin);

            if (lump.Type == TextureType.MipmapTexture)
            {
                var name = stream.ReadString(16);
                var width = (int)stream.ReadUint();
                var height = (int)stream.ReadUint();
                var offset = stream.ReadUint();
                var mipmap1Offset = stream.ReadUint();
                var mipmap2Offset = stream.ReadUint();
                var mipmap3Offset = stream.ReadUint();

                stream.Seek(lump.Offset + offset, SeekOrigin.Begin);
                var imageData = stream.ReadBytes(width * height);

                stream.Seek(lump.Offset + mipmap1Offset, SeekOrigin.Begin);
                var mipmap1Data = stream.ReadBytes(width / 2 * height / 2);

                stream.Seek(lump.Offset + mipmap2Offset, SeekOrigin.Begin);
                var mipmap2Data = stream.ReadBytes(width / 4 * height / 4);

                stream.Seek(lump.Offset + mipmap3Offset, SeekOrigin.Begin);
                var mipmap3Data = stream.ReadBytes(width / 8 * height / 8);

                var paletteSize = stream.ReadUshort();
                var palette = Enumerable.Range(0, paletteSize)
                    .Select(i => stream.ReadColor())
                    .ToArray();

                return Texture.CreateMipmapTexture(name, width, height, imageData, palette, mipmap1Data, mipmap2Data, mipmap3Data);
            }
            else if (lump.Type == TextureType.Font)
            {
                var width = (int)stream.ReadUint();
                var height = (int)stream.ReadUint();

                var rowCount = (int)stream.ReadUint();
                var rowHeight = (int)stream.ReadUint();
                var charInfos = Enumerable.Range(0, 256)
                    .Select(i => new CharInfo { StartOffset = stream.ReadUshort(), CharWidth = stream.ReadUshort() })
                    .ToArray();
                var imageData = stream.ReadBytes(width * height);

                var paletteSize = stream.ReadUshort();
                var palette = Enumerable.Range(0, paletteSize)
                    .Select(i => stream.ReadColor())
                    .ToArray();

                return Texture.CreateFont(lump.Name, width, height, rowCount, rowHeight, charInfos, imageData, palette);
            }
            else if (lump.Type == TextureType.SimpleTexture)
            {
                var width = (int)stream.ReadUint();
                var height = (int)stream.ReadUint();
                var imageData = stream.ReadBytes(width * height);

                var paletteSize = stream.ReadUshort();
                var palette = Enumerable.Range(0, paletteSize)
                    .Select(i => stream.ReadColor())
                    .ToArray();

                return Texture.CreateSimpleTexture(lump.Name, width, height, imageData, palette);
            }
            else
            {
                throw new InvalidDataException($"Unknown texture type: {lump.Type}.");
            }
        }

        private static uint GetTextureFileSize(Texture texture)
        {
            if (texture.Type == TextureType.MipmapTexture)
            {
                var size = 40;
                size += texture.ImageData.Length;
                size += texture.Mipmap1Data.Length;
                size += texture.Mipmap2Data.Length;
                size += texture.Mipmap3Data.Length;
                size += 2;
                size += texture.Palette.Length * 3;
                size += StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4);
                return (uint)size;
            }
            else if (texture.Type == TextureType.Font)
            {
                var size = 16;
                size += texture.CharInfos.Length * 4;
                size += texture.ImageData.Length;
                size += 2;
                size += texture.Palette.Length * 3;
                size += StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4);
                return (uint)size;
            }
            else if (texture.Type == TextureType.SimpleTexture)
            {
                var size = 8;
                size += texture.ImageData.Length;
                size += 2;
                size += texture.Palette.Length * 3;
                size += StreamExtensions.RequiredPadding(2 + texture.Palette.Length * 3, 4);
                return (uint)size;
            }
            else
            {
                throw new NotSupportedException($"Texture type {texture.Type} is not supported.");
            }
        }


        class Lump
        {
            public uint Offset { get; set; }
            public uint CompressedLength { get; set; }
            public uint FullLength { get; set; }
            public TextureType Type { get; set; }
            public byte CompressionType { get; set; }   // Always set to 0 (no compression).
            public string Name { get; set; }            // 16 bytes.
        }
    }
}
