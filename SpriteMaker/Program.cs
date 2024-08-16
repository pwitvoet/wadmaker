using Shared;
using Shared.FileFormats;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SpriteMaker
{
    class ProgramSettings
    {
        // General settings:
        public bool IncludeSubDirectories { get; set; }         // -subdirs         also processes files in sub-directories (applies to both sprite building and extracting)

        // Build settings:
        public bool FullRebuild { get; set; }                   // -full            forces a full rebuild instead of an incremental one
        public bool EnableSubDirectoryRemoval { get; set; }     // -subdirremoval   enables deleting of output sub-directories when input sub-directories are removed

        // Extract settings:
        public bool Extract { get; set; }                       // -extract         extracts all sprites in the input directory (this is also enabled if the input file is a .spr file)
        public bool ExtractAsSpriteSheet { get; set; }          // -spritesheet     extracts multi-frame sprites as spritesheets instead of image sequences
        public bool OverwriteExistingFiles { get; set; }        // -overwrite       extract mode only, enables overwriting of existing image files (off by default)
        public bool ExtractAsGif { get; set; }                  // -gif             extract sprites as (animated) gif files

        // Other settings:
        public string InputPath { get; set; } = "";             // An image or sprite file, or a directory full of images (or sprites, if -extract is set)
        public string OutputPath { get; set; } = "";            // Output sprite or image path, or output directory path

        public bool DisableFileLogging { get; set; }            // -nologfile       disables logging to a file (parent-directory\spritemaker.log)
    }

    enum ExtractionFormat
    {
        ImageSequence,
        Spritesheet,
        Gif,
    }

    class Program
    {
        static TextWriter? LogFile;


        static void Main(string[] args)
        {
            try
            {
                var assemblyName = Assembly.GetExecutingAssembly().GetName();
                var launchInfo = $"{assemblyName.Name}.exe (v{assemblyName.Version}) {string.Join(" ", args)}";
                Log(launchInfo);

                var settings = ParseArguments(args);
                if (!settings.DisableFileLogging)
                {
                    var logName = Path.GetFileNameWithoutExtension(settings.InputPath);
                    var logFilePath = Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? "", $"spritemaker - {logName}.log");
                    LogFile = new StreamWriter(logFilePath, false, Encoding.UTF8);
                    LogFile.WriteLine(launchInfo);
                }

                if (settings.Extract)
                {
                    var extractionFormat = settings.ExtractAsSpriteSheet ? ExtractionFormat.Spritesheet :
                                                   settings.ExtractAsGif ? ExtractionFormat.Gif :
                                                                           ExtractionFormat.ImageSequence;

                    if (!string.IsNullOrEmpty(Path.GetExtension(settings.InputPath)))
                        ExtractSingleSprite(settings.InputPath, settings.OutputPath, extractionFormat, settings.OverwriteExistingFiles);
                    else
                        ExtractSprites(settings.InputPath, settings.OutputPath, extractionFormat, settings.OverwriteExistingFiles, settings.IncludeSubDirectories);
                }
                else
                {
                    if (File.Exists(settings.InputPath))
                        MakeSingleSprite(settings.InputPath, settings.OutputPath);
                    else
                        MakeSprites(settings.InputPath, settings.OutputPath, settings.FullRebuild, settings.IncludeSubDirectories, settings.EnableSubDirectoryRemoval);
                }
            }
            catch (InvalidUsageException ex)
            {
                Log($"ERROR: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.GetType().Name}: '{ex.Message}'.");
                Log(ex.StackTrace);
            }
            finally
            {
                LogFile?.Dispose();
            }
        }

        static ProgramSettings ParseArguments(string[] args)
        {
            var settings = new ProgramSettings();

            // First parse options:
            var index = 0;
            while (index < args.Length && args[index].StartsWith("-"))
            {
                var arg = args[index++];
                switch (arg)
                {
                    case "-subdirs": settings.IncludeSubDirectories = true; break;
                    case "-full": settings.FullRebuild = true; break;
                    case "-subdirremoval": settings.EnableSubDirectoryRemoval = true; break;
                    case "-extract": settings.Extract = true; break;
                    case "-spritesheet": settings.ExtractAsSpriteSheet = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;
                    case "-gif": settings.ExtractAsGif = true; break;
                    case "-nologfile": settings.DisableFileLogging = true; break;
                    default: throw new InvalidUsageException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new InvalidUsageException("Missing input path (image or sprite file, or folder) argument.");

            if (File.Exists(paths[0]) && Path.GetExtension(paths[0]).ToLowerInvariant() == ".spr")
                settings.Extract = true;


            if (settings.Extract)
            {
                // Sprite extraction requires a spr file path or a directory:
                settings.InputPath = args[index++];

                if (index < args.Length)
                {
                    settings.OutputPath = args[index++];

                    if (Path.GetExtension(settings.OutputPath).ToLowerInvariant() == ".gif")
                        settings.ExtractAsGif = true;
                }
                else
                {
                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                    {
                        // By default, put the output image(s) in the same directory:
                        settings.OutputPath = Path.ChangeExtension(settings.InputPath, ".png");
                    }
                    else
                    {
                        // By default, put the output images in a '*_extracted' directory next to the input directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? "", Path.GetFileNameWithoutExtension(settings.InputPath) + "_extracted");
                    }
                }
            }
            else
            {
                // Sprite building requires an image file path or a directory:
                settings.InputPath = args[index++];

                if (index < args.Length)
                {
                    settings.OutputPath = args[index++];
                }
                else
                {
                    var inputIsFile = File.Exists(settings.InputPath);
                    if (inputIsFile)
                    {
                        // By default, put the output sprite in the same directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath)!, SpriteMakingSettings.GetSpriteName(settings.InputPath) + ".spr");
                    }
                    else
                    {
                        // By default, put output sprites in a '*_sprites' directory next to the input directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? "", Path.GetFileNameWithoutExtension(settings.InputPath) + "_sprites");
                    }
                }
            }

            return settings;
        }


        static void ExtractSprites(
            string inputDirectory,
            string outputDirectory,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles,
            bool includeSubDirectories)
        {
            var stopwatch = Stopwatch.StartNew();

            Log($"Extracting sprites from '{inputDirectory}' to '{outputDirectory}'.");

            (var spriteCount, var imageFilesCreated, var imageFilesSkipped) = ExtractSpritesFromDirectory(
                inputDirectory,
                outputDirectory,
                extractionFormat,
                overwriteExistingFiles,
                includeSubDirectories);

            Log($"Extracted {imageFilesCreated} images from {spriteCount} sprites from '{inputDirectory}' to '{outputDirectory}' (skipped {imageFilesSkipped} existing files), in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        static void ExtractSingleSprite(
            string inputPath,
            string outputPath,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles)
        {
            var stopwatch = Stopwatch.StartNew();

            Log($"Extracting sprite '{inputPath}' to '{outputPath}'.");

            (var success, var imageFilesCreated, var imageFilesSkipped) = ExtractSprite(inputPath, outputPath, extractionFormat, overwriteExistingFiles);

            Log($"Extracted '{inputPath}' to '{outputPath}' (created {imageFilesCreated} files, skipped {imageFilesSkipped} existing files), in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        static void MakeSprites(string inputDirectory, string outputDirectory, bool fullRebuild, bool includeSubDirectories, bool enableSubDirectoryRemoving)
        {
            var stopwatch = Stopwatch.StartNew();

            Log($"Creating sprites from '{inputDirectory}' and saving it to '{outputDirectory}'.");

            (var spritesAdded, var spritesUpdated, var spritesRemoved) = MakeSpritesFromImagesDirectory(inputDirectory, outputDirectory, fullRebuild, includeSubDirectories, enableSubDirectoryRemoving);

            Log($"Updated '{outputDirectory}' from '{inputDirectory}': added {spritesAdded}, updated {spritesUpdated} and removed {spritesRemoved} sprites, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        static void MakeSingleSprite(string inputPath, string outputPath)
        {
            var stopwatch = Stopwatch.StartNew();

            Log($"Creating a single sprite from '{inputPath}' and saving it to '{outputPath}'.");

            // Gather all related files and settings (for animated sprites, it's possible to use multiple frame-numbered images):
            var inputDirectory = Path.GetDirectoryName(inputPath)!;
            var spriteName = SpriteMakingSettings.GetSpriteName(inputPath);
            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory);
            var imagePaths = Directory.EnumerateFiles(inputDirectory)
                .Where(path => SpriteMakingSettings.GetSpriteName(path) == spriteName)
                .Where(path => ImageReading.IsSupported(path) || spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings.Converter != null)
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .ToArray();

            var conversionOutputDirectory = Path.Combine(inputDirectory, Guid.NewGuid().ToString());
            try
            {
                var success = MakeSprite(spriteName, imagePaths, outputPath, spriteMakingSettings, conversionOutputDirectory, true);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(conversionOutputDirectory))
                        Directory.Delete(conversionOutputDirectory, true);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            Log($"Created '{outputPath}' (from '{imagePaths.First()}'{(imagePaths.Length > 1 ? $" + {imagePaths.Length - 1} more files" : "")}) in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        // Sprite extraction:
        static (int spriteCount, int imageFilesCreated, int imageFilesSkipped) ExtractSpritesFromDirectory(
            string inputDirectory,
            string outputDirectory,
            ExtractionFormat extractionFormat,
            bool overwriteExistingFiles,
            bool includeSubDirectories)
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
                    overwriteExistingFiles);

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
                        includeSubDirectories);

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
            bool overwriteExistingFiles)
        {
            try
            {
                Log($"- Extracting '{inputPath}'...");

                var sprite = Sprite.Load(inputPath);
                var spriteFilenameSettings = new SpriteFilenameSettings {
                    Type = sprite.Type,
                    TextureFormat = sprite.TextureFormat
                };

                switch (extractionFormat)
                {
                    default:
                    case ExtractionFormat.ImageSequence:
                    {
                        return SaveSpriteAsImageSequence(sprite, outputPath, overwriteExistingFiles);
                    }

                    case ExtractionFormat.Spritesheet:
                    {
                        var success = SaveSpriteAsSpritesheet(sprite, outputPath, overwriteExistingFiles);
                        return (success, success ? 1 : 0, success ? 0 : 1);
                    }

                    case ExtractionFormat.Gif:
                    {
                        var success = SaveSpriteAsGif(sprite, outputPath, overwriteExistingFiles);
                        return (success, success ? 1 : 0, success ? 0 : 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"- WARNING: Failed to extract '{inputPath}': {ex.GetType().Name}: '{ex.Message}'.");
                return (false, 0, 0);
            }
        }

        static (bool success, int imageFilesCreated, int imageFilesSkipped) SaveSpriteAsImageSequence(Sprite sprite, string outputPath, bool overwriteExistingFiles)
        {
            var imageFilesSaved = 0;
            var imageFilesSkipped = 0;
            var imageEncoder = new PngEncoder { };

            for (int i = 0; i < sprite.Frames.Count; i++)
            {
                var spriteFrame = sprite.Frames[i];

                var spriteFilenameSettings = new SpriteFilenameSettings {
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
                    Log($"- Creating image file '{imageOutputPath}'.");
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
                    Log($"- Skipping image file '{imageOutputPath}' because it already exists.");
                    imageFilesSkipped += 1;
                }
            }

            return (true, imageFilesSaved, imageFilesSkipped);
        }

        static bool SaveSpriteAsSpritesheet(Sprite sprite, string outputPath, bool overwriteExistingFiles)
        {
            var spriteFilenameSettings = new SpriteFilenameSettings {
                Type = sprite.Type,
                TextureFormat = sprite.TextureFormat,
                SpritesheetTileSize = new Size((int)sprite.MaximumWidth, (int)sprite.MaximumHeight),
            };

            var spritesheetOutputPath = Path.ChangeExtension(spriteFilenameSettings.InsertIntoFilename(outputPath), ".png");
            if (!overwriteExistingFiles && File.Exists(spritesheetOutputPath))
            {
                Log($"- Skipping image file '{spritesheetOutputPath}' because it already exists.");
                return false;
            }

            // NOTE: SpriteMaker does not support setting custom frame offsets in spritesheets. It's possible to add padding to each tile,
            //       but with very large origin values that could produce extremely large spritesheets with lots of wasted space.
            //       So frames with custom origins will just be cut off here. For these kind of sprites image sequences should be used instead.

            Log($"- Creating image file '{spritesheetOutputPath}'.");
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

        static bool SaveSpriteAsGif(Sprite sprite, string outputPath, bool overwriteExistingFiles)
        {
            var spriteFilenameSettings = new SpriteFilenameSettings {
                Type = sprite.Type,
                TextureFormat = sprite.TextureFormat,
                SpritesheetTileSize = new Size((int)sprite.MaximumWidth, (int)sprite.MaximumHeight),
            };

            var gifOutputPath = Path.ChangeExtension(spriteFilenameSettings.InsertIntoFilename(outputPath), ".gif");
            if (!overwriteExistingFiles && File.Exists(gifOutputPath))
            {
                Log($"- Skipping image file '{gifOutputPath}' because it already exists.");
                return false;
            }

            // NOTE: Support for custom frame offsets is limited when extracting as gif, for the same reasons as with spritesheets.

            Log($"- Creating image file '{gifOutputPath}'.");
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
                image.Save(gifOutputPath, new GifEncoder {
                    ColorTableMode = GifColorTableMode.Global,
                    PixelSamplingStrategy = new ExtensivePixelSamplingStrategy(), // TODO: Is this needed??
                    Quantizer = new PaletteQuantizer(new ReadOnlyMemory<Color>(sprite.Palette.Select(rgba32 => new Color(rgba32)).ToArray()), new QuantizerOptions {
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


        // Sprite making:
        static (int spritesAdded, int spritesUpdated, int spritesRemoved) MakeSpritesFromImagesDirectory(
            string inputDirectory,
            string outputDirectory,
            bool fullRebuild,
            bool includeSubDirectories,
            bool enableSubDirectoryRemoving)
        {
            var spritesAdded = 0;
            var spritesUpdated = 0;
            var spritesRemoved = 0;

            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory);
            var currentFileHashes = new Dictionary<string, byte[]?>();
            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);

            CreateDirectory(outputDirectory);

            // Multiple files can map to the same sprite, due to different extensions, filename suffixes and upper/lower-case differences.
            // We'll group files by sprite name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory, "*").ToHashSet();
            var spriteImagePaths = allInputDirectoryFiles
                .Where(path => ImageReading.IsSupported(path) || spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings.Converter != null)
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .GroupBy(path => SpriteMakingSettings.GetSpriteName(path));

            try
            {
                // Loop over all the groups of input images (each group, if valid, will produce one output sprite):
                foreach (var imagePathsGroup in spriteImagePaths)
                {
                    var spriteName = imagePathsGroup.Key;
                    var outputSpritePath = Path.Combine(outputDirectory, spriteName + ".spr");
                    var isExistingSprite = File.Exists(outputSpritePath);

                    var success = MakeSprite(
                        spriteName,
                        imagePathsGroup,
                        outputSpritePath,
                        spriteMakingSettings,
                        conversionOutputDirectory,
                        fullRebuild,
                        currentFileHashes);

                    if (success)
                    {
                        var inputImageCount = imagePathsGroup.Count();
                        if (isExistingSprite)
                        {
                            spritesUpdated += 1;
                            Log($"- Updated sprite '{outputSpritePath}' (from '{imagePathsGroup.First()}'{(inputImageCount > 1 ? $" + {inputImageCount - 1} more files" : "")}).");
                        }
                        else
                        {
                            spritesAdded += 1;
                            Log($"- Added sprite '{outputSpritePath}' (from '{imagePathsGroup.First()}'{(inputImageCount > 1 ? $" + {inputImageCount - 1} more files" : "")}).");
                        }
                    }
                }

                // Remove sprites whose source images have been removed:
                var oldSpriteNames = spriteMakingSettings.FileHashesHistory
                    .Select(kv => SpriteMakingSettings.GetSpriteName(kv.Key))
                    .ToHashSet();
                var newSpriteNames = spriteImagePaths
                    .Select(group => group.Key)
                    .ToHashSet();
                foreach (var spriteName in oldSpriteNames)
                {
                    if (!newSpriteNames.Contains(spriteName))
                    {
                        var spriteFilePath = Path.Combine(outputDirectory, spriteName + ".spr");
                        try
                        {
                            if (File.Exists(spriteFilePath))
                            {
                                File.Delete(spriteFilePath);
                                spritesRemoved += 1;
                                Log($"- Removed sprite '{spriteFilePath}'.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"- WARNING: Failed to remove '{spriteFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(conversionOutputDirectory))
                        Directory.Delete(conversionOutputDirectory, true);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            // Handle sub-directories (recursively):
            var currentSubDirectoryNames = new HashSet<string>();
            if (includeSubDirectories)
            {
                foreach (var subDirectoryPath in Directory.EnumerateDirectories(inputDirectory))
                {
                    if (ExternalConversion.IsConversionOutputDirectory(subDirectoryPath))
                        continue;

                    var subDirectoryName = Path.GetFileName(subDirectoryPath);
                    (var added, var updated, var removed) = MakeSpritesFromImagesDirectory(
                        subDirectoryPath,
                        Path.Combine(outputDirectory, subDirectoryName),
                        fullRebuild,
                        includeSubDirectories,
                        enableSubDirectoryRemoving);

                    currentSubDirectoryNames.Add(subDirectoryName);
                    spritesAdded += added;
                    spritesUpdated += updated;
                    spritesRemoved += removed;
                }

                if (enableSubDirectoryRemoving)
                {
                    // Remove output sprites for sub-directories that have been removed:
                    foreach (var subDirectoryName in spriteMakingSettings.SubDirectoryNamesHistory)
                    {
                        // Remove all sprites from the associated output directory, and the directory itself as well if it's empty:
                        if (!currentSubDirectoryNames.Contains(subDirectoryName))
                            spritesRemoved += RemoveOutputSprites(Path.Combine(outputDirectory, subDirectoryName));
                    }
                }
            }

            spriteMakingSettings.UpdateHistory(currentFileHashes, currentSubDirectoryNames);

            return (spritesAdded, spritesUpdated, spritesRemoved);
        }

        /// <summary>
        /// Creates and saves a sprite file from the given input images and settings.
        /// If <param name="forceRebuild"/> is false, and <paramref name="previousFileHashes"/> and <paramref name="currentFileHashes"/> are provided,
        /// then this method will skip making a sprite if it already exists and is up-to-date. It will then also update <paramref name="currentFileHashes"/>
        /// with the file hashes of the given input images.
        /// </summary>
        static bool MakeSprite(
            string spriteName,
            IEnumerable<string> imagePaths,
            string outputSpritePath,
            SpriteMakingSettings spriteMakingSettings,
            string conversionOutputDirectory,
            bool forceRebuild,
            IDictionary<string, byte[]?>? currentFileHashes = null)
        {
            try
            {
                var imagePathsAndSettings = imagePaths
                    .Select(path =>
                    {
                        var isSupportedFileType = ImageReading.IsSupported(path);
                        var filenameSettings = SpriteFilenameSettings.FromFilename(path);
                        var spriteSettings = spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path));

                        // Filename settings take priority over config files:
                        if (filenameSettings.Type != null)          spriteSettings.settings.SpriteType = filenameSettings.Type;
                        if (filenameSettings.TextureFormat != null) spriteSettings.settings.SpriteTextureFormat = filenameSettings.TextureFormat;
                        if (filenameSettings.FrameOffset != null)   spriteSettings.settings.FrameOffset = filenameSettings.FrameOffset;

                        return (path, isSupportedFileType, filenameSettings, spriteSettings);
                    })
                    .OrderBy(file => file.filenameSettings.FrameNumber)
                    .ToArray();

                if (imagePathsAndSettings.Any(file => !file.isSupportedFileType && file.spriteSettings.settings.ConverterArguments == null))
                {
                    Log($"WARNING: some input files for '{spriteName}' are missing converter arguments. Skipping sprite.");
                    return false;
                }
                else if (imagePaths.Count() > 1 && imagePathsAndSettings.Any(file => file.filenameSettings.FrameNumber == null))
                {
                    Log($"WARNING: not all input files for '{spriteName}' contain a frame number ({string.Join(", ", imagePaths)}). Skipping sprite.");
                    return false;
                }

                // Read file hashes - these are used to detect filename changes, and will be stored for future change detection:
                if (currentFileHashes != null)
                {
                    var imageFileHashes = imagePaths.ToDictionary(path => Path.GetFileName(path)!, GetFileHash);
                    foreach (var kv in imageFileHashes)
                        currentFileHashes[kv.Key] = kv.Value;

                    // Do we need to update this sprite?
                    if (!forceRebuild)
                    {
                        var spriteFileInfo = new FileInfo(outputSpritePath);
                        if (spriteFileInfo.Exists)
                        {
                            var lastSpriteUpdateTime = spriteFileInfo.LastWriteTimeUtc;

                            // Have any settings been updated? Have any source images been updated? Have any frame images been swapped or has any file been renamed?
                            if (!imagePathsAndSettings.Any(file => file.spriteSettings.lastUpdate > lastSpriteUpdateTime) &&
                                !imagePathsAndSettings.Any(file => new FileInfo(file.path).LastWriteTimeUtc > lastSpriteUpdateTime) &&
                                imageFileHashes.All(kv => spriteMakingSettings.FileHashesHistory.TryGetValue(kv.Key, out var oldHash) && IsEqualHash(oldHash, kv.Value)))
                            {
                                // No changes detected, this sprite doesn't need to be rebuilt:
                                return false;
                            }
                        }
                    }
                }

                // Start building this sprite:
                using (var frameImages = new DisposableList<FrameImage>())
                {
                    foreach (var file in imagePathsAndSettings)
                    {
                        // Do we need to convert this image?
                        var initialImageFilePath = file.path;
                        var imageFilePaths = new[] { initialImageFilePath };
                        var spriteSettings = file.spriteSettings.settings;
                        if (spriteSettings.Converter != null)
                        {
                            if (spriteSettings.ConverterArguments == null)
                                throw new InvalidDataException($"Unable to convert '{file.path}': missing converter arguments.");

                            initialImageFilePath = Path.Combine(conversionOutputDirectory, Path.GetFileNameWithoutExtension(file.path));
                            CreateDirectory(conversionOutputDirectory);

                            var outputFilePaths = ExternalConversion.ExecuteConversionCommand(spriteSettings.Converter, spriteSettings.ConverterArguments, file.path, initialImageFilePath, Log);
                            if (outputFilePaths.Length < 1)
                                throw new IOException("Unable to find converter output files. Output files must have the same name as the input file (different extensions and suffixes are ok).");

                            imageFilePaths = outputFilePaths.Where(ImageReading.IsSupported).ToArray();
                            if (imageFilePaths.Length < 1)
                                throw new IOException("The converter did not produce any supported file types.");
                        }

                        // Load images (and cut up spritesheets into separate frame images):
                        foreach (var imageFilePath in imageFilePaths)
                        {
                            var image = ImageReading.ReadImage(imageFilePath);
                            if (file.filenameSettings.SpritesheetTileSize is Size tileSize)
                            {
                                if (tileSize.Width < 1 || tileSize.Height < 1)
                                    throw new InvalidDataException($"Invalid tile size for image '{file.path}' ({tileSize.Width} x {tileSize.Height}): tile size must not be negative.");
                                if (image.Width % tileSize.Width != 0 || image.Height % tileSize.Height != 0)
                                    throw new InvalidDataException($"Spritesheet image '{file.path}' size ({image.Width} x {image.Height}) is not a multiple of the specified tile size ({tileSize.Width} x {tileSize.Height}).");

                                var tileImages = GetSpritesheetTiles(image, tileSize);
                                foreach (var tileImage in tileImages)
                                    frameImages.Add(new FrameImage(tileImage, file.spriteSettings.settings, frameImages.Count));

                                image.Dispose();
                            }
                            else
                            {
                                frameImages.Add(new FrameImage(image, file.spriteSettings.settings, file.filenameSettings.FrameNumber ?? frameImages.Count));
                            }
                        }
                    }

                    // Sprite settings:
                    var firstFile = imagePathsAndSettings.First();
                    var spriteType = firstFile.filenameSettings.Type ?? firstFile.spriteSettings.settings.SpriteType ?? SpriteType.Parallel;
                    var spriteTextureFormat = firstFile.filenameSettings.TextureFormat ?? firstFile.spriteSettings.settings.SpriteTextureFormat ?? SpriteTextureFormat.Additive;

                    var sprite = CreateSpriteFromImages(frameImages, spriteType, spriteTextureFormat);
                    sprite.Save(outputSpritePath);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: Failed to build '{spriteName}': {ex.GetType().Name}: '{ex.Message}'.");
                return false;
            }
        }

        static int RemoveOutputSprites(string directory)
        {
            if (!Directory.Exists(directory))
                return 0;

            var spritesRemoved = 0;

            // First remove all sprite files:
            foreach (var spriteFilePath in Directory.EnumerateFiles(directory, "*.spr"))
            {
                try
                {
                    File.Delete(spriteFilePath);
                    spritesRemoved += 1;
                }
                catch (Exception ex)
                {
                    Log($"Failed to remove '{spriteFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            // Then recursively try removing sub-directories:
            foreach (var subDirectoryPath in Directory.EnumerateDirectories(directory))
                spritesRemoved += RemoveOutputSprites(subDirectoryPath);

            try
            {
                // Finally, remove this directory, but only if it's now empty:
                if (!Directory.EnumerateFiles(directory).Any() && !Directory.EnumerateDirectories(directory).Any())
                    Directory.Delete(directory);

                Log($"Removed sub-directory '{directory}'.");
            }
            catch (Exception ex)
            {
                Log($"Failed to remove sub-directory '{directory}': {ex.GetType().Name}: '{ex.Message}'.");
            }

            return spritesRemoved;
        }

        static Image<Rgba32>[] GetSpritesheetTiles(Image<Rgba32> spritesheet, Size tileSize)
        {
            // Frames are taken from left to right, then from top to bottom.
            var frameImages = new List<Image<Rgba32>>();
            for (int y = 0; y + tileSize.Height <= spritesheet.Height; y += tileSize.Height)
            {
                for (int x = 0; x + tileSize.Width <= spritesheet.Width; x += tileSize.Width)
                {
                    var frameImage = spritesheet.Clone(context => context.Crop(new Rectangle(x, y, tileSize.Width, tileSize.Height)));
                    frameImages.Add(frameImage);
                }
            }
            return frameImages.ToArray();
        }

        static Sprite CreateSpriteFromImages(IList<FrameImage> frameImages, SpriteType spriteType, SpriteTextureFormat spriteTextureFormat)
        {
            (var palette, var colorIndexMappingCache) = CreatePaletteAndColorIndexMappingCache(frameImages, spriteTextureFormat);

            // Create the sprite and its frames:
            var spriteWidth = frameImages.Max(frameImage => frameImage.Image.Frames.OfType<ImageFrame<Rgba32>>().Max(frame => frame.Width));
            var spriteHeight = frameImages.Max(frameImage => frameImage.Image.Frames.OfType<ImageFrame<Rgba32>>().Max(frame => frame.Height));
            var isAnimatedSprite = frameImages.Count() > 1 || frameImages[0].Image.Frames.Count > 1;

            var sprite = Sprite.CreateSprite(spriteType, spriteTextureFormat, spriteWidth, spriteHeight, palette);
            foreach (var frameImage in frameImages)
            {
                var image = frameImage.Image;
                foreach (ImageFrame<Rgba32> frame in image.Frames)
                {
                    sprite.Frames.Add(new Frame(
                        FrameType.Single,
                        -(frame.Width / 2) + (frameImage.Settings.FrameOffset?.X ?? 0),
                        (frame.Height / 2) + (frameImage.Settings.FrameOffset?.Y ?? 0),
                        (uint)frame.Width,
                        (uint)frame.Height,
                        CreateFrameImageData(
                            frame,
                            palette,
                            colorIndexMappingCache,
                            spriteTextureFormat,
                            frameImage.Settings,
                            MakeTransparencyPredicate(frameImage, spriteTextureFormat == SpriteTextureFormat.AlphaTest),
                            disableDithering: isAnimatedSprite)));
                }
            }
            return sprite;
        }

        static (Rgba32[] palette, Dictionary<Rgba32, int> colorIndexMappingCache) CreatePaletteAndColorIndexMappingCache(
            IList<FrameImage> frameImages,
            SpriteTextureFormat spriteTextureFormat)
        {
            if (spriteTextureFormat == SpriteTextureFormat.IndexAlpha)
            {
                Rgba32 decalColor;
                if (frameImages.First().Settings.IndexAlphaColor is Rgba32 indexAlphaColor)
                {
                    decalColor = indexAlphaColor;
                }
                else
                {
                    var colorHistogram = ColorQuantization.GetColorHistogram(
                        frameImages.SelectMany(frameImage => frameImage.Image.Frames.OfType<ImageFrame<Rgba32>>()),
                        color => color.A == 0);
                    decalColor = ColorQuantization.GetAverageColor(colorHistogram);
                }
                var palette = Enumerable.Range(0, 255)
                    .Select(i => new Rgba32((byte)i, (byte)i, (byte)i))
                    .Append(decalColor)
                    .ToArray();

                // NOTE: No need to map colors to palette indexes, because the index can easily be derived from the color itself
                // //    (either the alpha channel or the average RGB channel values):
                return (palette, new Dictionary<Rgba32, int>());
            }
            else
            {
                // Create a single color histogram from all frame images:
                var colorHistogram = new Dictionary<Rgba32, int>();
                var isAlphaTest = spriteTextureFormat == SpriteTextureFormat.AlphaTest;
                foreach (var frameImage in frameImages)
                {
                    foreach (ImageFrame<Rgba32> frame in frameImage.Image.Frames)
                        ColorQuantization.UpdateColorHistogram(colorHistogram, frame, MakeTransparencyPredicate(frameImage, isAlphaTest));
                }

                // Create a suitable palette, taking sprite texture format into account:
                var maxColors = isAlphaTest ? 255 : 256;
                var colorClusters = ColorQuantization.GetColorClusters(colorHistogram, maxColors);

                // Always make sure we've got a 256-color palette (some tools can't handle smaller palettes):
                if (colorClusters.Length < maxColors)
                {
                    colorClusters = colorClusters
                        .Concat(Enumerable
                            .Range(0, maxColors - colorClusters.Length)
                            .Select(i => (new Rgba32(), new[] { new Rgba32() })))
                        .ToArray();
                }

                if (isAlphaTest)
                {
                    var colorKey = new Rgba32(0, 0, 255);
                    colorClusters = colorClusters
                        .Append((colorKey, new[] { colorKey }))         // Slot 255: used for transparent pixels
                        .ToArray();
                }


                // Create the actual palette, and a color index lookup cache:
                var palette = colorClusters
                    .Select(cluster => cluster.averageColor)
                    .ToArray();
                var colorIndexMappingCache = new Dictionary<Rgba32, int>();
                for (int i = 0; i < colorClusters.Length; i++)
                {
                    foreach (var color in colorClusters[i].colors)
                        colorIndexMappingCache[color] = i;
                }

                return (palette, colorIndexMappingCache);
            }
        }

        static Func<Rgba32, bool> MakeTransparencyPredicate(FrameImage frameImage, bool isAlphaTest)
        {
            var transparencyThreshold = isAlphaTest ? Clamp(frameImage.Settings.AlphaTestTransparencyThreshold ?? 128, 0, 255) : 0;
            if (frameImage.Settings.AlphaTestTransparencyColor is Rgba32 transparencyColor)
                return color => color.A < transparencyThreshold || (color.R == transparencyColor.R && color.G == transparencyColor.G && color.B == transparencyColor.B);

            return color => color.A < transparencyThreshold;
        }

        static byte[] CreateFrameImageData(
            ImageFrame<Rgba32> imageFrame,
            Rgba32[] palette,
            IDictionary<Rgba32, int> colorIndexMappingCache,
            SpriteTextureFormat spriteTextureFormat,
            SpriteSettings spriteSettings,
            Func<Rgba32, bool> isTransparent,
            bool disableDithering)
        {
            Func<Rgba32, int> getColorIndex;
            if (spriteTextureFormat == SpriteTextureFormat.IndexAlpha)
            {
                disableDithering = true;

                if (spriteSettings.IndexAlphaTransparencySource == IndexAlphaTransparencySource.Grayscale)
                    getColorIndex = color => (color.R + color.G + color.B) / 3;
                else
                    getColorIndex = color => color.A;
            }
            else
            {
                getColorIndex = ColorQuantization.CreateColorIndexLookup(palette, colorIndexMappingCache, isTransparent);
            }

            var ditheringAlgorithm = spriteSettings.DitheringAlgorithm ?? (disableDithering ? DitheringAlgorithm.None : DitheringAlgorithm.FloydSteinberg);
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None:
                    return ApplyPaletteWithoutDithering();

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(imageFrame, palette, getColorIndex, spriteSettings.DitherScale ?? 0.75f, isTransparent);
            }


            byte[] ApplyPaletteWithoutDithering()
            {
                var textureData = new byte[imageFrame.Width * imageFrame.Height];
                imageFrame.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < imageFrame.Height; y++)
                    {
                        var rowSpan = accessor.GetRowSpan(y);
                        for (int x = 0; x < imageFrame.Width; x++)
                        {
                            var color = rowSpan[x];
                            textureData[y * imageFrame.Width + x] = (byte)getColorIndex(color);
                        }
                    }
                });
                return textureData;
            }
        }


        static byte[] GetFileHash(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha256 = SHA256.Create())
                return sha256.ComputeHash(file);
        }

        static bool IsEqualHash(byte[]? hash1, byte[]? hash2) => hash1 != null && hash2 != null && Enumerable.SequenceEqual(hash1, hash2);

        // TODO: Move this to a common place in Shared -- it's duplicated 3 times now!
        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

        static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }


        static void Log(string? message)
        {
            Console.WriteLine(message);
            LogFile?.WriteLine(message);
        }
    }
}
