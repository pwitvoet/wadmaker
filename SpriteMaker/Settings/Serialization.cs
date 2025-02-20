using Shared;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SpriteMaker.Settings
{
    static class Serialization
    {
        public static string ToString(SpriteType spriteType)
        {
            switch (spriteType)
            {
                case SpriteType.ParallelUpright: return "parallel-upright";
                case SpriteType.Upright: return "upright";
                default:
                case SpriteType.Parallel: return "parallel";
                case SpriteType.Oriented: return "oriented";
                case SpriteType.ParallelOriented: return "parallel-oriented";
            }
        }

        public static SpriteType? ReadSpriteType(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: throw new InvalidDataException($"Invalid sprite type: '{str}'.");
                case "parallel-upright": return SpriteType.ParallelUpright;
                case "upright": return SpriteType.Upright;
                case "parallel": return SpriteType.Parallel;
                case "oriented": return SpriteType.Oriented;
                case "parallel-oriented": return SpriteType.ParallelOriented;
            }
        }


        public static string ToString(SpriteTextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case SpriteTextureFormat.Normal: return "normal";
                default:
                case SpriteTextureFormat.Additive: return "additive";
                case SpriteTextureFormat.IndexAlpha: return "index-alpha";
                case SpriteTextureFormat.AlphaTest: return "alpha-test";
            }
        }

        public static SpriteTextureFormat? ReadSpriteTextureFormat(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: throw new InvalidDataException($"Invalid sprite texture format: '{str}'.");
                case "normal": return SpriteTextureFormat.Normal;
                case "additive": return SpriteTextureFormat.Additive;
                case "index-alpha": return SpriteTextureFormat.IndexAlpha;
                case "alpha-test": return SpriteTextureFormat.AlphaTest;
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


        public static string ToString(IndexAlphaTransparencySource transparencySource)
        {
            switch (transparencySource)
            {
                default:
                case IndexAlphaTransparencySource.AlphaChannel: return "alpha";
                case IndexAlphaTransparencySource.Grayscale: return "grayscale";
            }
        }

        public static IndexAlphaTransparencySource? ReadIndexAlphaTransparencySource(string? str)
        {
            if (str is null)
                return null;

            switch (str.ToLowerInvariant())
            {
                default: throw new InvalidDataException($"Invalid index-alpha transparency: '{str}'.");
                case "alpha": return IndexAlphaTransparencySource.AlphaChannel;
                case "grayscale": return IndexAlphaTransparencySource.Grayscale;
            }
        }


        public static string ToString(Rgba32 color) => color.ToHex();

        public static Rgba32? ReadRgba32(string? str) => str is null ? null : Rgba32.ParseHex(str);


        public static string ToString(Point point) => $"{point.X}, {point.Y}";

        public static Point? ReadPoint(string? str)
        {
            if (str is null)
                return null;

            var parts = str.Split(',');
            return new Point(int.Parse(parts[0]), int.Parse(parts[1]));
        }


        public static string ToString(Size size) => $"{size.Width}, {size.Height}";

        public static Size? ReadSize(string? str)
        {
            if (str is null)
                return null;

            var parts = str.Split(',');
            return new Size(int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }
}
