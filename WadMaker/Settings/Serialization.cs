using Shared;
using SixLabors.ImageSharp.PixelFormats;

namespace WadMaker.Settings
{
    static class Serialization
    {
        public static string ToString(TextureType textureType)
        {
            switch (textureType)
            {
                default:
                case TextureType.MipmapTexture: return "mipmap";
                case TextureType.SimpleTexture: return "qpic";
                case TextureType.Font: return "font";
            }
        }

        public static TextureType? ReadTextureType(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: throw new InvalidDataException($"Invalid texture type: '{str}'.");
                case "mipmap": return TextureType.MipmapTexture;
                case "qpic": return TextureType.SimpleTexture;
                case "font": return TextureType.Font;
            }
        }


        public static string ToString(MipmapLevel mipmapLevel)
        {
            switch (mipmapLevel)
            {
                default:
                case MipmapLevel.Main: return "";
                case MipmapLevel.Mipmap1: return "mipmap1";
                case MipmapLevel.Mipmap2: return "mipmap2";
                case MipmapLevel.Mipmap3: return "mipmap3";
            }
        }

        public static MipmapLevel? ReadMipmapLevel(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: return MipmapLevel.Main;
                case "mipmap1": return MipmapLevel.Mipmap1;
                case "mipmap2": return MipmapLevel.Mipmap2;
                case "mipmap3": return MipmapLevel.Mipmap3;
            }
        }


        public static string ToString(DitheringAlgorithm ditheringAlgorithm)
        {
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None: return "none";
                case DitheringAlgorithm.FloydSteinberg: return "floyd-steinberg";
            }
        }

        public static DitheringAlgorithm? ReadDitheringAlgorithm(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: throw new InvalidDataException($"Invalid dithering algorithm: '{str}'.");
                case "none": return DitheringAlgorithm.None;
                case "floyd-steinberg": return DitheringAlgorithm.FloydSteinberg;
            }
        }


        public static string ToString(DecalTransparencySource decalTransparencySource)
        {
            switch (decalTransparencySource)
            {
                default:
                case DecalTransparencySource.AlphaChannel: return "alpha";
                case DecalTransparencySource.Grayscale: return "grayscale";
            }
        }

        public static DecalTransparencySource? ReadDecalTransparencySource(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: throw new InvalidDataException($"Invalid decal transparency: '{str}'.");
                case "alpha": return DecalTransparencySource.AlphaChannel;
                case "grayscale": return DecalTransparencySource.Grayscale;
            }
        }


        public static string ToString(Rgba32 color) => color.ToHex();

        public static Rgba32? ReadRgba32(string? str) => str is null ? null : Rgba32.ParseHex(str);
    }
}
