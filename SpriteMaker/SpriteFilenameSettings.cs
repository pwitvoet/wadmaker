using Shared.Sprites;
using SixLabors.ImageSharp;
using System.IO;
using System.Linq;

namespace SpriteMaker
{
    /// <summary>
    /// Some sprite and frame settings can be controlled directly with input image filenames by adding dot-separated segments at the end of the filename, before the extension.
    /// For example, "fire.pu.ia.32x64.png" will result in a parallel-upright (.pu), index-alpha (.ia) sprite file named "fire.spr",
    /// and each 32x64 tile from the input image produces a separate frame.
    /// </summary>
    struct SpriteFilenameSettings
    {
        public SpriteType? Type { get; set; }
        public SpriteTextureFormat? TextureFormat { get; set; }
        public Size? SpritesheetTileSize { get; set; }
        public int? FrameNumber { get; set; }
        public Point? FrameOffset { get; set; }


        public static SpriteFilenameSettings FromFilename(string path)
        {
            var settings = new SpriteFilenameSettings();
            foreach (var segment in Path.GetFileNameWithoutExtension(path)
                .Split('.')
                .Skip(1)
                .Select(segment => segment.Trim().ToLowerInvariant()))
            {
                if (SpriteMakingSettings.TryParseSpriteType(segment, out var type))
                    settings.Type = type;
                else if (SpriteMakingSettings.TryParseSpriteTextureFormat(segment, out var textureFormat))
                    settings.TextureFormat = textureFormat;
                else if (TryParseSpritesheetTileSize(segment, out var spritesheetTileSize))
                    settings.SpritesheetTileSize = spritesheetTileSize;
                else if (int.TryParse(segment, out var frameNumber))
                    settings.FrameNumber = frameNumber;
                else if (TryParseFrameOffset(segment, out var frameOffset))
                    settings.FrameOffset = frameOffset;
            }
            return settings;
        }

        private static bool TryParseSpritesheetTileSize(string str, out Size size)
        {
            var parts = str.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var width) && int.TryParse(parts[1].Trim(), out var height))
            {
                size = new Size(width, height);
                return true;
            }
            else
            {
                size = default;
                return false;
            }
        }

        private static bool TryParseFrameOffset(string str, out Point point)
        {
            if (str.StartsWith("@"))
            {
                var parts = str.Substring(1).Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var x) && int.TryParse(parts[1].Trim(), out var y))
                {
                    point = new Point(x, y);
                    return true;
                }
            }

            point = default;
            return false;
        }
    }
}
