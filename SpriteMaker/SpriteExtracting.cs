using Shared.Sprites;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using SixLabors.ImageSharp;
using System.Diagnostics;
using Shared;

namespace SpriteMaker
{
    public enum ExtractionFormat
    {
        ImageSequence,
        Spritesheet,
        Gif,
    }

    public static class SpriteExtracting
    {
        public static void ExtractSprites(
            string inputDirectory,
            string outputDirectory,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles,
            bool includeSubDirectories,
            Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Extracting sprites from '{inputDirectory}' to '{outputDirectory}'.");

            (var spriteCount, var imageFilesCreated, var imageFilesSkipped) = ExtractSpritesFromDirectory(
                inputDirectory,
                outputDirectory,
                extractionFormat,
                overwriteExistingFiles,
                includeSubDirectories,
                logger);

            logger.Log($"Extracted {imageFilesCreated} images from {spriteCount} sprites from '{inputDirectory}' to '{outputDirectory}' (skipped {imageFilesSkipped} existing files), in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        public static void ExtractSingleSprite(
            string inputPath,
            string outputPath,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles,
            Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Extracting sprite '{inputPath}' to '{outputPath}'.");

            (var success, var imageFilesCreated, var imageFilesSkipped) = ExtractSprite(inputPath, outputPath, extractionFormat, overwriteExistingFiles, logger);

            logger.Log($"Extracted '{inputPath}' to '{outputPath}' (created {imageFilesCreated} files, skipped {imageFilesSkipped} existing files), in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        static (int spriteCount, int imageFilesCreated, int imageFilesSkipped) ExtractSpritesFromDirectory(
            string inputDirectory,
            string outputDirectory,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles,
            bool includeSubDirectories,
            Logger logger)
        {
            var spriteCount = 0;
            var imageFilesCreated = 0;
            var imageFilesSkipped = 0;

            CreateDirectory(outputDirectory);

            foreach (var path in Directory.EnumerateFiles(inputDirectory, "*.spr"))
            {
                (var success, var imagesCreated, var imagesSkipped) = ExtractSprite(
                    path,
                    Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path) + ".spr"),
                    extractionFormat,
                    overwriteExistingFiles,
                    logger);

                if (success)
                {
                    spriteCount += 1;
                    imageFilesCreated += imagesCreated;
                    imageFilesSkipped += imagesSkipped;
                }
            }

            if (includeSubDirectories)
            {
                foreach (var subDirectory in Directory.EnumerateDirectories(inputDirectory))
                {
                    (var subSpriteCount, var subFilesCreated, var imagesSkipped) = ExtractSpritesFromDirectory(
                        subDirectory,
                        Path.Combine(outputDirectory, Path.GetFileName(subDirectory)),
                        extractionFormat,
                        overwriteExistingFiles,
                        includeSubDirectories,
                        logger);

                    spriteCount += subSpriteCount;
                    imageFilesCreated += subFilesCreated;
                    imageFilesSkipped += imagesSkipped;
                }
            }

            return (spriteCount, imageFilesCreated, imageFilesSkipped);
        }

        static (bool success, int imageFilesCreated, int imageFilesSkipped) ExtractSprite(
            string inputPath,
            string outputPath,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles,
            Logger logger)
        {
            try
            {
                logger.Log($"- Extracting '{inputPath}'...");

                var sprite = Sprite.Load(inputPath);

                switch (extractionFormat)
                {
                    default:
                    case ExtractionFormat.ImageSequence:
                    {
                        return SaveSpriteAsImageSequence(sprite, outputPath, overwriteExistingFiles, logger);
                    }

                    case ExtractionFormat.Spritesheet:
                    {
                        var success = SaveSpriteAsSpritesheet(sprite, outputPath, overwriteExistingFiles, logger);
                        return (success, success ? 1 : 0, success ? 0 : 1);
                    }

                    case ExtractionFormat.Gif:
                    {
                        var success = SaveSpriteAsGif(sprite, outputPath, overwriteExistingFiles, logger);
                        return (success, success ? 1 : 0, success ? 0 : 1);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"- WARNING: Failed to extract '{inputPath}': {ex.GetType().Name}: '{ex.Message}'.");
                return (false, 0, 0);
            }
        }

        static (bool success, int imageFilesCreated, int imageFilesSkipped) SaveSpriteAsImageSequence(Sprite sprite, string outputPath, bool overwriteExistingFiles, Logger logger)
        {
            var imageFilesSaved = 0;
            var imageFilesSkipped = 0;
            var imageEncoder = new PngEncoder { };

            for (int i = 0; i < sprite.Frames.Count; i++)
            {
                var spriteFrame = sprite.Frames[i];

                var spriteFilenameSettings = new SpriteFilenameSettings
                {
                    Type = sprite.Type,
                    TextureFormat = sprite.TextureFormat,
                    FrameNumber = i,
                };

                var offset = new Point(spriteFrame.FrameOriginX + ((int)spriteFrame.FrameWidth / 2), spriteFrame.FrameOriginY - ((int)spriteFrame.FrameHeight / 2));
                if (offset.X != 0 || offset.Y != 0)
                    spriteFilenameSettings.FrameOffset = offset;

                var imageOutputPath = Path.ChangeExtension(spriteFilenameSettings.InsertIntoFilename(outputPath), ".png");
                if (overwriteExistingFiles || !File.Exists(imageOutputPath))
                {
                    logger.Log($"- Creating image file '{imageOutputPath}'.");
                    using (var image = new Image<Rgba32>((int)spriteFrame.FrameWidth, (int)spriteFrame.FrameHeight))
                    {
                        var imageFrame = image.Frames[0];
                        CopySpriteFrameToImageFrame(
                            spriteFrame,
                            new Rectangle(0, 0, (int)spriteFrame.FrameWidth, (int)spriteFrame.FrameHeight),
                            imageFrame,
                            new Rectangle(0, 0, imageFrame.Width, imageFrame.Height),
                            sprite.TextureFormat,
                            sprite.Palette);

                        image.Save(imageOutputPath, imageEncoder);
                        imageFilesSaved += 1;
                    }
                }
                else
                {
                    logger.Log($"- Skipping image file '{imageOutputPath}' because it already exists.");
                    imageFilesSkipped += 1;
                }
            }

            return (true, imageFilesSaved, imageFilesSkipped);
        }

        static bool SaveSpriteAsSpritesheet(Sprite sprite, string outputPath, bool overwriteExistingFiles, Logger logger)
        {
            var spriteFilenameSettings = new SpriteFilenameSettings
            {
                Type = sprite.Type,
                TextureFormat = sprite.TextureFormat,
                SpritesheetTileSize = new Size((int)sprite.MaximumWidth, (int)sprite.MaximumHeight),
            };

            var spritesheetOutputPath = Path.ChangeExtension(spriteFilenameSettings.InsertIntoFilename(outputPath), ".png");
            if (!overwriteExistingFiles && File.Exists(spritesheetOutputPath))
            {
                logger.Log($"- Skipping image file '{spritesheetOutputPath}' because it already exists.");
                return false;
            }

            // NOTE: SpriteMaker does not support setting custom frame offsets in spritesheets. It's possible to add padding to each tile,
            //       but with very large origin values that could produce extremely large spritesheets with lots of wasted space.
            //       So frames with custom origins will just be cut off here. For these kind of sprites image sequences should be used instead.

            logger.Log($"- Creating image file '{spritesheetOutputPath}'.");
            using (var image = new Image<Rgba32>((int)sprite.MaximumWidth * sprite.Frames.Count, (int)sprite.MaximumHeight))
            {
                for (int i = 0; i < sprite.Frames.Count; i++)
                {
                    var spriteFrame = sprite.Frames[i];
                    var tileArea = new Rectangle(i * (int)sprite.MaximumWidth, 0, (int)sprite.MaximumWidth, (int)sprite.MaximumHeight);
                    (var sourceArea, var destinationArea) = GetFrameSourceAndDestinationAreas(spriteFrame, tileArea);

                    CopySpriteFrameToImageFrame(
                        spriteFrame,
                        sourceArea,
                        image.Frames[0],
                        destinationArea,
                        sprite.TextureFormat,
                        sprite.Palette);
                }

                image.Save(spritesheetOutputPath, new PngEncoder { });
            }

            return true;
        }

        static bool SaveSpriteAsGif(Sprite sprite, string outputPath, bool overwriteExistingFiles, Logger logger)
        {
            var spriteFilenameSettings = new SpriteFilenameSettings
            {
                Type = sprite.Type,
                TextureFormat = sprite.TextureFormat,
                SpritesheetTileSize = new Size((int)sprite.MaximumWidth, (int)sprite.MaximumHeight),
            };

            var gifOutputPath = Path.ChangeExtension(spriteFilenameSettings.InsertIntoFilename(outputPath), ".gif");
            if (!overwriteExistingFiles && File.Exists(gifOutputPath))
            {
                logger.Log($"- Skipping image file '{gifOutputPath}' because it already exists.");
                return false;
            }

            // NOTE: Support for custom frame offsets is limited when extracting as gif, for the same reasons as with spritesheets.

            logger.Log($"- Creating image file '{gifOutputPath}'.");
            using (var image = new Image<Rgba32>((int)sprite.MaximumWidth, (int)sprite.MaximumHeight))
            {
                while (image.Frames.Count < sprite.Frames.Count)
                    image.Frames.CreateFrame();

                for (int i = 0; i < sprite.Frames.Count; i++)
                {
                    var spriteFrame = sprite.Frames[i];
                    (var sourceArea, var destinationArea) = GetFrameSourceAndDestinationAreas(spriteFrame, new Rectangle(0, 0, image.Width, image.Height));

                    CopySpriteFrameToImageFrame(
                        spriteFrame,
                        sourceArea,
                        image.Frames[i],
                        destinationArea,
                        sprite.TextureFormat,
                        sprite.Palette);
                }

                image.Metadata.GetFormatMetadata(GifFormat.Instance).RepeatCount = 0;
                image.Save(gifOutputPath, new GifEncoder
                {
                    ColorTableMode = GifColorTableMode.Global,
                    PixelSamplingStrategy = new ExtensivePixelSamplingStrategy(), // TODO: Is this needed??
                    Quantizer = new PaletteQuantizer(new ReadOnlyMemory<Color>(sprite.Palette.Select(rgba32 => new Color(rgba32)).ToArray()), new QuantizerOptions
                    {
                        MaxColors = 256,
                        Dither = null
                    }),
                });
            }

            return true;
        }

        static (Rectangle sourceArea, Rectangle destinationArea) GetFrameSourceAndDestinationAreas(Frame frame, Rectangle tileArea)
        {
            // Frame offset (most frames have an offset of (0, 0), which means they're put in the center):
            var frameOffset = new Point(frame.FrameOriginX + ((int)frame.FrameWidth / 2), frame.FrameOriginY - ((int)frame.FrameHeight / 2));

            // Offset, relative to tile top-left corner:
            var offsetX = (tileArea.Width - (int)frame.FrameWidth) / 2 + frameOffset.X;
            var offsetY = (tileArea.Height - (int)frame.FrameHeight) / 2 - frameOffset.Y;

            var sourceArea = new Rectangle(
                Math.Max(0, -offsetX),
                Math.Max(0, -offsetY),
                Math.Min(tileArea.Width - Math.Max(0, offsetX), (int)frame.FrameWidth - Math.Max(0, -offsetX)),
                Math.Min(tileArea.Height - Math.Max(0, offsetY), (int)frame.FrameHeight - Math.Max(0, -offsetY)));

            var destinationArea = new Rectangle(
                tileArea.X + Math.Max(0, offsetX),
                tileArea.Y + Math.Max(0, offsetY),
                sourceArea.Width,
                sourceArea.Height);

            return (sourceArea, destinationArea);
        }

        static void CopySpriteFrameToImageFrame(
            Frame spriteFrame,
            Rectangle sourceArea,
            ImageFrame<Rgba32> imageFrame,
            Rectangle destinationArea,
            SpriteTextureFormat textureFormat,
            Rgba32[] palette)
        {
            if (sourceArea.Width > destinationArea.Width || sourceArea.Height > destinationArea.Height)
                throw new InvalidOperationException($"Cannot copy sprite frame, image frame too small (must be at least {sourceArea.Width}x{sourceArea.Height} but is {destinationArea.Width}x{destinationArea.Height}).");
            if (palette.Length < 256)
                throw new ArgumentException($"Palette must contain 256 colors, but contains only {palette.Length} colors.");

            var getColor = (textureFormat == SpriteTextureFormat.IndexAlpha) ? (Func<byte, Rgba32>)GetIndexAlphaColor :
                            (textureFormat == SpriteTextureFormat.AlphaTest) ? (Func<byte, Rgba32>)GetAlphaTestColor :
                                                                               (Func<byte, Rgba32>)GetPaletteColor;

            imageFrame.ProcessPixelRows(accessor =>
            {
                for (int dy = 0; dy < sourceArea.Height; dy++)
                {
                    var rowSpan = accessor.GetRowSpan(destinationArea.Y + dy);
                    for (int dx = 0; dx < sourceArea.Width; dx++)
                    {
                        var index = spriteFrame.ImageData[(sourceArea.Y + dy) * spriteFrame.FrameWidth + (sourceArea.X + dx)];
                        rowSpan[destinationArea.X + dx] = getColor(index);
                    }
                }
            });


            Rgba32 GetIndexAlphaColor(byte index)
            {
                var color = palette[255];
                color.A = index;
                return color;
            }
            Rgba32 GetAlphaTestColor(byte index) => (index == 255) ? new Rgba32(0, 0, 0, 0) : palette[index];
            Rgba32 GetPaletteColor(byte index) => palette[index];
        }


        static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
