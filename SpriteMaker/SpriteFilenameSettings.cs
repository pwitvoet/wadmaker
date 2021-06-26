﻿using Shared.Sprites;
using SixLabors.ImageSharp;
using System.IO;
using System.Linq;

namespace SpriteMaker
{
    struct SpriteFilenameSettings
    {
        public SpriteType? Type { get; set; }
        public SpriteTextureFormat? TextureFormat { get; set; }
        public Size? SpritesheetTileSize { get; set; }
        public int? FrameNumber { get; set; }


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
    }
}
