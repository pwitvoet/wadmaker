using Shared.Sprites;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using SixLabors.ImageSharp;
using System.Diagnostics;
using Shared;
using Shared.FileFormats;
using Shared.FileFormats.Indexed;
using SpriteMaker.Settings;

namespace SpriteMaker
{
    public enum ExtractionFormat
    {
        ImageSequence,
        Spritesheet,
        AnimatedGif,
    }

    public class ExtractionSettings
    {
        public bool OverwriteExistingFiles { get; set; }
        public bool IncludeSubDirectories { get; set; }

        public ExtractionFormat ExtractionFormat { get; set; }
        public ImageFormat OutputFormat { get; set; }
        public bool SaveAsIndexed { get; set; }
    }


    public static class SpriteExtracting
    {
        public static void ExtractSprites(
            string inputDirectory,
            string outputDirectory,
            ExtractionSettings extractionSettings,
            Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Extracting sprites from '{inputDirectory}' to '{outputDirectory}'.");

            (var spriteCount, var imageFilesCreated, var imageFilesSkipped) = ExtractSpritesFromDirectory(
                inputDirectory,
                outputDirectory,
                extractionSettings,
                logger);

            logger.Log($"Extracted {imageFilesCreated} images from {spriteCount} sprites from '{inputDirectory}' to '{outputDirectory}' (skipped {imageFilesSkipped} existing files), in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        public static void ExtractSingleSprite(
            string inputPath,
            string outputPath,
            ExtractionSettings extractionSettings,
            Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Extracting sprite '{inputPath}' to '{outputPath}'.");

            (var success, var imageFilesCreated, var imageFilesSkipped) = ExtractSprite(inputPath, outputPath, extractionSettings, logger);

            logger.Log($"Extracted '{inputPath}' to '{outputPath}' (created {imageFilesCreated} files, skipped {imageFilesSkipped} existing files), in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        private static (int spriteCount, int imageFilesCreated, int imageFilesSkipped) ExtractSpritesFromDirectory(
            string inputDirectory,
            string outputDirectory,
            ExtractionSettings extractionSettings,
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
                    Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path) + "." + ImageFileIO.GetDefaultExtension(extractionSettings.OutputFormat)),
                    extractionSettings,
                    logger);

                if (success)
                {
                    spriteCount += 1;
                    imageFilesCreated += imagesCreated;
                    imageFilesSkipped += imagesSkipped;
                }
            }

            if (extractionSettings.IncludeSubDirectories)
            {
                foreach (var subDirectory in Directory.EnumerateDirectories(inputDirectory))
                {
                    (var subSpriteCount, var subFilesCreated, var imagesSkipped) = ExtractSpritesFromDirectory(
                        subDirectory,
                        Path.Combine(outputDirectory, Path.GetFileName(subDirectory)),
                        extractionSettings,
                        logger);

                    spriteCount += subSpriteCount;
                    imageFilesCreated += subFilesCreated;
                    imageFilesSkipped += imagesSkipped;
                }
            }

            return (spriteCount, imageFilesCreated, imageFilesSkipped);
        }

        private static (bool success, int imageFilesCreated, int imageFilesSkipped) ExtractSprite(
            string inputPath,
            string outputPath,
            ExtractionSettings extractionSettings,
            Logger logger)
        {
            try
            {
                logger.Log($"- Extracting '{inputPath}'...");

                var sprite = Sprite.Load(inputPath);

                switch (extractionSettings.ExtractionFormat)
                {
                    default:
                    case ExtractionFormat.ImageSequence:
                    {
                        return SaveSpriteAsImageSequence(sprite, outputPath, extractionSettings, logger);
                    }

                    case ExtractionFormat.Spritesheet:
                    {
                        var success = SaveSpriteAsSpritesheet(sprite, outputPath, extractionSettings, logger);
                        return (success, success ? 1 : 0, success ? 0 : 1);
                    }

                    case ExtractionFormat.AnimatedGif:
                    {
                        var success = SaveSpriteAsGif(sprite, outputPath, extractionSettings, logger);
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

        private static (bool success, int imageFilesCreated, int imageFilesSkipped) SaveSpriteAsImageSequence(Sprite sprite, string outputPath, ExtractionSettings extractionSettings, Logger logger)
        {
            var imageFilesSaved = 0;
            var imageFilesSkipped = 0;

            for (int i = 0; i < sprite.Frames.Count; i++)
            {
                var spriteFrame = sprite.Frames[i];

                var filenameSettings = new SpriteSettings {
                    SpriteType = sprite.Type,
                    SpriteTextureFormat = sprite.TextureFormat,
                };

                if (sprite.Frames.Count > 1)
                    filenameSettings.FrameNumber = i;

                var offset = new Point(spriteFrame.FrameOriginX + ((int)spriteFrame.FrameWidth / 2), spriteFrame.FrameOriginY - ((int)spriteFrame.FrameHeight / 2));
                if (offset.X != 0 || offset.Y != 0)
                    filenameSettings.FrameOffset = offset;

                var imageOutputPath = SpriteMakingSettings.InsertSpriteSettingsIntoFilename(outputPath, filenameSettings);
                if (extractionSettings.OverwriteExistingFiles || !File.Exists(imageOutputPath))
                {
                    logger.Log($"- Creating image file '{imageOutputPath}'.");

                    if (extractionSettings.SaveAsIndexed)
                    {
                        var indexedImage = new IndexedImage(spriteFrame.ImageData, (int)spriteFrame.FrameWidth, (int)spriteFrame.FrameHeight, sprite.Palette);
                        ImageFileIO.SaveIndexedImage(indexedImage, imageOutputPath, extractionSettings.OutputFormat);
                        imageFilesSaved += 1;
                    }
                    else
                    {
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

                            ImageFileIO.SaveImage(image, imageOutputPath, extractionSettings.OutputFormat);
                            imageFilesSaved += 1;
                        }
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

        private static bool SaveSpriteAsSpritesheet(Sprite sprite, string outputPath, ExtractionSettings extractionSettings, Logger logger)
        {
            var filenameSettings = new SpriteSettings {
                SpriteType = sprite.Type,
                SpriteTextureFormat = sprite.TextureFormat,
                SpritesheetTileSize = new Size((int)sprite.MaximumWidth, (int)sprite.MaximumHeight),
            };

            var spritesheetOutputPath = SpriteMakingSettings.InsertSpriteSettingsIntoFilename(outputPath, filenameSettings);
            if (!extractionSettings.OverwriteExistingFiles && File.Exists(spritesheetOutputPath))
            {
                logger.Log($"- Skipping image file '{spritesheetOutputPath}' because it already exists.");
                return false;
            }

            // NOTE: SpriteMaker does not support setting custom frame offsets in spritesheets. It's possible to add padding to each tile,
            //       but with very large origin values that could produce extremely large spritesheets with lots of wasted space.
            //       So frames with custom origins will just be cut off here. For these kind of sprites image sequences should be used instead.

            logger.Log($"- Creating image file '{spritesheetOutputPath}'.");

            var tileWidth = (int)sprite.MaximumWidth;
            var tileHeight = (int)sprite.MaximumHeight;
            var spritesheetWidth = tileWidth * sprite.Frames.Count;
            var spritesheetHeight = tileHeight;

            if (extractionSettings.SaveAsIndexed)
            {
                var indexedImage = new IndexedImage(new byte[spritesheetWidth * spritesheetHeight], spritesheetWidth, spritesheetHeight, sprite.Palette);
                for (int i = 0; i < sprite.Frames.Count; i++)
                {
                    var spriteFrame = sprite.Frames[i];
                    var tileArea = new Rectangle(i * tileWidth, 0, tileWidth, tileHeight);
                    (var sourceArea, var destinationArea) = GetFrameSourceAndDestinationAreas(spriteFrame, tileArea);

                    CopySpriteFrameToIndexedImageFrame(spriteFrame, sourceArea, indexedImage.Frames[0], destinationArea);
                }

                ImageFileIO.SaveIndexedImage(indexedImage, spritesheetOutputPath, extractionSettings.OutputFormat);
            }
            else
            {
                using (var image = new Image<Rgba32>(spritesheetWidth, spritesheetHeight))
                {
                    for (int i = 0; i < sprite.Frames.Count; i++)
                    {
                        var spriteFrame = sprite.Frames[i];
                        var tileArea = new Rectangle(i * tileWidth, 0, tileWidth, tileHeight);
                        (var sourceArea, var destinationArea) = GetFrameSourceAndDestinationAreas(spriteFrame, tileArea);

                        CopySpriteFrameToImageFrame(
                            spriteFrame,
                            sourceArea,
                            image.Frames[0],
                            destinationArea,
                            sprite.TextureFormat,
                            sprite.Palette);
                    }

                    ImageFileIO.SaveImage(image, spritesheetOutputPath, extractionSettings.OutputFormat);
                }
            }

            return true;
        }

        private static bool SaveSpriteAsGif(Sprite sprite, string outputPath, ExtractionSettings extractionSettings, Logger logger)
        {
            var filenameSettings = new SpriteSettings {
                SpriteType = sprite.Type,
                SpriteTextureFormat = sprite.TextureFormat,
                SpritesheetTileSize = new Size((int)sprite.MaximumWidth, (int)sprite.MaximumHeight),
            };

            var gifOutputPath = SpriteMakingSettings.InsertSpriteSettingsIntoFilename(Path.ChangeExtension(outputPath, ".gif"), filenameSettings);
            if (!extractionSettings.OverwriteExistingFiles && File.Exists(gifOutputPath))
            {
                logger.Log($"- Skipping image file '{gifOutputPath}' because it already exists.");
                return false;
            }

            // NOTE: Support for custom frame offsets is limited when extracting as gif, for the same reasons as with spritesheets.

            logger.Log($"- Creating image file '{gifOutputPath}'.");

            // NOTE: Gif files are always indexed.
            // TODO: The PaletteQuantizer that is used here has limited accuracy, so this is a lossy process!
            //       Unfortunately, it's not possible to save the sprite data exactly as-is with the current version of ImageSharp:
            //       that would require a custom quantizer (possible) and the gif encoder would have to use that quantizer for all frames
            //       (currently it only uses it for the first frame, and it then uses a PaletteQuantizer for all subsequent frames).
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
                    PixelSamplingStrategy = new ExtensivePixelSamplingStrategy(),
                    // TODO: This quantizer lops off the lowest 3 bits of each channel, so colors that are close together will be lumped together!
                    Quantizer = new PaletteQuantizer(new ReadOnlyMemory<Color>(sprite.Palette.Select(rgba32 => new Color(rgba32)).ToArray()), new QuantizerOptions
                    {
                        MaxColors = 256,
                        Dither = null
                    }),
                });
            }

            return true;
        }

        private static (Rectangle sourceArea, Rectangle destinationArea) GetFrameSourceAndDestinationAreas(Frame frame, Rectangle tileArea)
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

        private static void CopySpriteFrameToImageFrame(
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

        private static void CopySpriteFrameToIndexedImageFrame(
            Frame spriteFrame,
            Rectangle sourceArea,
            IndexedImageFrame indexedImageFrame,
            Rectangle destinationArea)
        {
            if (sourceArea.Width > destinationArea.Width || sourceArea.Height > destinationArea.Height)
                throw new InvalidOperationException($"Cannot copy sprite frame, image frame too small (must be at least {sourceArea.Width}x{sourceArea.Height} but is {destinationArea.Width}x{destinationArea.Height}).");

            for (int dy = 0; dy < sourceArea.Height; dy++)
            {
                for (int dx = 0; dx < sourceArea.Width; dx++)
                {
                    var index = spriteFrame.ImageData[(sourceArea.Y + dy) * spriteFrame.FrameWidth + (sourceArea.X + dx)];
                    indexedImageFrame[destinationArea.X + dx, destinationArea.Y + dy] = index;
                }
            }
        }


        private static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
