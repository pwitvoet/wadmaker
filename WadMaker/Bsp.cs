using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WadMaker
{
    public class Bsp
    {
        public static List<Texture> GetEmbeddedTextures(string path)
        {
            using (var file = File.OpenRead(path))
                return GetEmbeddedTextures(file);
        }

        public static List<Texture> GetEmbeddedTextures(Stream stream)
        {
            var bspVersion = stream.ReadInt();
            if (bspVersion != 30)
                throw new NotSupportedException("Only BSP v30 is supported.");

            stream.Seek(16, SeekOrigin.Current);
            var textureLumpOffset = stream.ReadInt();

            stream.Seek(textureLumpOffset, SeekOrigin.Begin);
            var textureCount = stream.ReadInt();
            var textureOffsets = Enumerable.Range(0, textureCount)
                .Select(i => stream.ReadInt())
                .ToArray();

            var textures = new List<Texture>();
            for (int i = 0; i < textureCount; i++)
            {
                stream.Seek(textureLumpOffset + textureOffsets[i], SeekOrigin.Begin);

                var name = stream.ReadString(16);
                var width = (int)stream.ReadUint();
                var height = (int)stream.ReadUint();
                var imageDataOffsets = Enumerable.Range(0, 4)
                    .Select(i => stream.ReadUint())
                    .ToArray();

                // Is this an embedded texture?
                if (imageDataOffsets[0] == 0)
                    continue;

                var imageData = new byte[4][];
                for (int j = 0; j < 4; j++)
                {
                    if (imageDataOffsets[j] == 0)
                        continue;

                    stream.Seek(textureLumpOffset + textureOffsets[i] + imageDataOffsets[j], SeekOrigin.Begin);
                    imageData[j] = stream.ReadBytes((width * height) >> (j * 2));
                }
                var paletteSize = stream.ReadUshort();
                var palette = Enumerable.Range(0, paletteSize)
                    .Select(i => stream.ReadColor())
                    .ToArray();

                textures.Add(Texture.CreateMipmapTexture(name, width, height, imageData[0], palette, imageData[1], imageData[2], imageData[3]));
            }
            return textures;
        }
    }
}
