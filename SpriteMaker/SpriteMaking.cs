using Shared.FileFormats;
using Shared.Sprites;
using Shared;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using SpriteMaker.Settings;
using FileInfo = Shared.FileSystem.FileInfo;
using Shared.FileSystem;
using Shared.FileFormats.Indexed;

namespace SpriteMaker
{
    public static class SpriteMaking
    {
        public static void MakeSprites(string inputDirectory, string outputDirectory, bool fullRebuild, bool includeSubDirectories, bool enableSubDirectoryRemoving, Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Creating sprites from '{inputDirectory}' and saving it to '{outputDirectory}'.");

            (var spritesAdded, var spritesUpdated, var spritesRemoved) = MakeSpritesFromImagesDirectory(inputDirectory, outputDirectory, fullRebuild, includeSubDirectories, enableSubDirectoryRemoving, logger);

            logger.Log($"Updated '{outputDirectory}' from '{inputDirectory}': added {spritesAdded}, updated {spritesUpdated} and removed {spritesRemoved} sprites, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        public static void MakeSingleSprite(string inputPath, string outputPath, Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Creating a single sprite from '{inputPath}' and saving it to '{outputPath}'.");

            // Gather all related files and settings (for animated sprites, it's possible to use multiple frame-numbered images):
            var inputDirectory = Path.GetDirectoryName(inputPath)!;
            var spriteName = SpriteMakingSettings.GetSpriteName(inputPath);
            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory);
            var conversionOutputDirectory = Path.Combine(inputDirectory, Guid.NewGuid().ToString());

            // Gather input files:
            var spriteSourceFiles = GetInputFilePaths(inputDirectory)
                .Where(path => SpriteMakingSettings.GetSpriteName(path) == spriteName)
                .Select(spriteMakingSettings.GetSpriteSourceFileInfo)
                .Where(file => file.Settings.Ignore != true && (ImageFileIO.CanLoad(file.Path) || !string.IsNullOrEmpty(file.Settings.Converter)))
                .ToArray();

            try
            {
                var success = MakeSprite(spriteName, spriteSourceFiles, outputPath, spriteMakingSettings, conversionOutputDirectory, logger);
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
                    logger.Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            logger.Log($"Created '{outputPath}' (from '{spriteSourceFiles.First().Path}'{(spriteSourceFiles.Length > 1 ? $" + {spriteSourceFiles.Length - 1} more files" : "")}) in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        private static (int spritesAdded, int spritesUpdated, int spritesRemoved) MakeSpritesFromImagesDirectory(
            string inputDirectory,
            string outputDirectory,
            bool doFullRebuild,
            bool includeSubDirectories,
            bool enableSubDirectoryRemoving,
            Logger logger)
        {
            // We can do an incremental build if we have information about the previous build operation, and if the output directory already exists:
            var spriteMakingHistory = SpriteMakingHistory.Load(inputDirectory);
            var doIncrementalUpdate = !doFullRebuild && Directory.Exists(outputDirectory) && spriteMakingHistory != null;

            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory);
            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);

            // Gather input files:
            var spriteSourceFileGroups = GetInputFilePaths(inputDirectory)
                .Select(spriteMakingSettings.GetSpriteSourceFileInfo)
                .Where(file => file.Settings.Ignore != true && (ImageFileIO.CanLoad(file.Path) || !string.IsNullOrEmpty(file.Settings.Converter)))
                .GroupBy(file => SpriteMakingSettings.GetSpriteName(file.Path))
                .ToArray();

            // Keep track of some statistics, to keep the user informed:
            var spritesAdded = 0;
            var spritesUpdated = 0;
            var spritesRemoved = 0;

            CreateDirectory(outputDirectory);

            var successfulSpriteInputs = new Dictionary<string, SpriteMakingHistory.SpriteHistory>();

            try
            {
                // Create sprites and save them to the output directory:
                foreach (var spriteSourceFileGroup in spriteSourceFileGroups)
                {
                    var spriteName = spriteSourceFileGroup.Key;
                    var spriteSourceFiles = spriteSourceFileGroup.ToArray();

                    var outputSpritePath = Path.Combine(outputDirectory, spriteName + ".spr");
                    var isExistingSprite = File.Exists(outputSpritePath);

                    // Can we skip this sprite (when updating an existing output directory)?
                    if (doIncrementalUpdate && isExistingSprite && !HasBeenModified(spriteName, outputSpritePath, spriteSourceFiles, spriteMakingHistory))
                    {
                        var outputFile = FileInfo.FromFile(outputSpritePath);
                        successfulSpriteInputs[spriteName] = new SpriteMakingHistory.SpriteHistory(outputFile, spriteSourceFiles);

                        logger.Log($"- No changes detected for '{spriteName}', skipping update.");
                        continue;
                    }

                    var success = MakeSprite(
                        spriteName,
                        spriteSourceFiles,
                        outputSpritePath,
                        spriteMakingSettings,
                        conversionOutputDirectory,
                        logger);

                    if (success)
                    {
                        var outputFile = FileInfo.FromFile(outputSpritePath);
                        successfulSpriteInputs[spriteName] = new SpriteMakingHistory.SpriteHistory(outputFile, spriteSourceFiles);

                        if (isExistingSprite)
                        {
                            spritesUpdated += 1;
                            logger.Log($"- Updated sprite '{outputSpritePath}' (from '{spriteSourceFiles.First().Path}'{(spriteSourceFiles.Length > 1 ? $" + {spriteSourceFiles.Length - 1} more files" : "")}).");
                        }
                        else
                        {
                            spritesAdded += 1;
                            logger.Log($"- Added sprite '{outputSpritePath}' (from '{spriteSourceFiles.First().Path}'{(spriteSourceFiles.Length > 1 ? $" + {spriteSourceFiles.Length - 1} more files" : "")}).");
                        }
                    }
                }

                // Remove sprites whose source images have been removed:
                if (spriteMakingHistory != null)
                {
                    var oldSpriteNames = spriteMakingHistory.Sprites
                        .Select(kv => kv.Key)
                        .ToHashSet();
                    var newSpriteNames = spriteSourceFileGroups
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
                                    logger.Log($"- Removed sprite '{spriteFilePath}'.");
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Log($"- WARNING: Failed to remove '{spriteFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                            }
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
                    logger.Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
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
                        doFullRebuild,
                        includeSubDirectories,
                        enableSubDirectoryRemoving,
                        logger);

                    currentSubDirectoryNames.Add(subDirectoryName);
                    spritesAdded += added;
                    spritesUpdated += updated;
                    spritesRemoved += removed;
                }

                if (enableSubDirectoryRemoving && spriteMakingHistory is not null)
                {
                    // Remove output sprites for sub-directories that have been removed:
                    foreach (var subDirectoryName in spriteMakingHistory.SubDirectoryNames)
                    {
                        // Remove all sprites from the associated output directory, and the directory itself as well if it's empty:
                        if (!currentSubDirectoryNames.Contains(subDirectoryName))
                            spritesRemoved += RemoveOutputSprites(Path.Combine(outputDirectory, subDirectoryName), logger);
                    }
                }
            }

            // Save information about this build operation, to enable future incremental updates:
            var newHistory = new SpriteMakingHistory(successfulSpriteInputs, currentSubDirectoryNames);
            newHistory.Save(inputDirectory);

            return (spritesAdded, spritesUpdated, spritesRemoved);
        }

        private static bool HasBeenModified(string spriteName, string outputSpritePath, SpriteSourceFileInfo[] spriteSourceFiles, SpriteMakingHistory? spriteMakingHistory)
        {
            if (spriteMakingHistory is null || !spriteMakingHistory.Sprites.TryGetValue(spriteName, out var spriteHistory))
                return true;

            if (spriteSourceFiles.Length != spriteHistory.InputFiles.Length)
                return true;

            if (!spriteHistory.OutputFile.HasMatchingFileHash(outputSpritePath))
                return true;

            foreach (var sourceFile in spriteSourceFiles)
            {
                var sourceFileName = Path.GetFileName(sourceFile.Path);
                var previousSourceFile = spriteHistory.InputFiles.FirstOrDefault(file => Path.GetFileName(file.Path) == sourceFileName);
                if (previousSourceFile is null)
                    return true;

                if (sourceFile.FileSize != previousSourceFile.FileSize || sourceFile.FileHash != previousSourceFile.FileHash || sourceFile.Settings != previousSourceFile.Settings)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Creates and saves a sprite file from the given input images and settings.
        /// If <param name="forceRebuild"/> is false, and <paramref name="previousFileHashes"/> and <paramref name="currentFileHashes"/> are provided,
        /// then this method will skip making a sprite if it already exists and is up-to-date. It will then also update <paramref name="currentFileHashes"/>
        /// with the file hashes of the given input images.
        /// </summary>
        private static bool MakeSprite(
            string spriteName,
            SpriteSourceFileInfo[] sourceFiles,
            string outputSpritePath,
            SpriteMakingSettings spriteMakingSettings,
            string conversionOutputDirectory,
            Logger logger)
        {
            try
            {
                if (sourceFiles.Any(sourceFile => sourceFile.Settings.Converter is not null && sourceFile.Settings.ConverterArguments is null))
                {
                    logger.Log($"WARNING: some input files for '{spriteName}' are missing converter arguments. Skipping sprite.");
                    return false;
                }

                // First gather all input files, converting any if necessary:
                var convertedSourceFiles = new List<SpriteSourceFileInfo>();
                for (int i = 0; i < sourceFiles.Length; i++)
                {
                    var sourceFile = sourceFiles[i];
                    if (sourceFile.Settings.Converter is null)
                    {
                        convertedSourceFiles.Add(sourceFile);
                    }
                    else
                    {
                        if (sourceFile.Settings.ConverterArguments is null)
                            throw new InvalidUsageException($"Unable to convert '{sourceFile.Path}': missing converter arguments.");

                        var conversionOutputPath = Path.Combine(conversionOutputDirectory, spriteName);
                        CreateDirectory(conversionOutputDirectory);

                        var outputFilePaths = ExternalConversion.ExecuteConversionCommand(sourceFile.Settings.Converter, sourceFile.Settings.ConverterArguments, sourceFile.Path, conversionOutputPath, logger);
                        if (outputFilePaths.Length < 1)
                            throw new IOException("Unable to find converter output file. An output file must have the same name as the input file (different extensions are ok).");

                        var supportedOutputFilePaths = outputFilePaths.Where(ImageFileIO.CanLoad).ToArray();
                        if (supportedOutputFilePaths.Length < 1)
                            throw new IOException("The converter did not produce a supported file type.");

                        foreach (var supportedOutputFilePath in supportedOutputFilePaths)
                        {
                            var settings = new SpriteSettings(sourceFile.Settings);
                            settings.OverrideWith(SpriteMakingSettings.GetSpriteSettingsFromFilename(supportedOutputFilePath));
                            convertedSourceFiles.Add(new SpriteSourceFileInfo(supportedOutputFilePath, 0, new FileHash(), DateTimeOffset.UtcNow, settings));
                        }
                    }
                }

                if (convertedSourceFiles.Count > 1 && convertedSourceFiles.Any(sourceFile => sourceFile.Settings.FrameNumber is null))
                {
                    logger.Log($"WARNING: not all input files for '{spriteName}' contain a frame number ({string.Join(", ", convertedSourceFiles.Select(sourceFile => sourceFile.Path))}). Skipping sprite.");
                    return false;
                }

                // Order by frame number:
                var orderedSourceFiles = convertedSourceFiles
                    .OrderBy(sourceFile => sourceFile.Settings.FrameNumber)
                    .ToArray();

                // Do we need to preserve the source image's palette?
                if (orderedSourceFiles[0].Settings.PreservePalette == true && orderedSourceFiles.All(sourceFile => ImageFileIO.IsIndexed(sourceFile.Path)))
                {
                    var sprite = CreateSpriteFromIndexedSourceFiles(spriteName, orderedSourceFiles, logger);
                    sprite.Save(outputSpritePath);
                    return true;
                }


                // Start building this sprite:
                using (var frameImages = new DisposableList<FrameImage>())
                {
                    foreach (var sourceFile in orderedSourceFiles)
                    {
                        var image = ImageFileIO.LoadImage(sourceFile.Path /*imageFilePath*/);
                        if (sourceFile.Settings.SpritesheetTileSize is Size tileSize)
                        {
                            if (tileSize.Width < 1 || tileSize.Height < 1)
                                throw new InvalidDataException($"Invalid tile size for image '{sourceFile.Path}' ({tileSize.Width} x {tileSize.Height}): tile size must not be negative.");
                            if (image.Width % tileSize.Width != 0 || image.Height % tileSize.Height != 0)
                                throw new InvalidDataException($"Spritesheet image '{sourceFile.Path}' size ({image.Width} x {image.Height}) is not a multiple of the specified tile size ({tileSize.Width} x {tileSize.Height}).");

                            var tileImages = GetSpritesheetTiles(image, tileSize);
                            foreach (var tileImage in tileImages)
                                frameImages.Add(new FrameImage(tileImage, sourceFile.Settings, frameImages.Count));

                            image.Dispose();
                        }
                        else
                        {
                            frameImages.Add(new FrameImage(image, sourceFile.Settings, sourceFile.Settings.FrameNumber ?? frameImages.Count));
                        }
                    }

                    // Sprite settings:
                    var firstSourceFile = orderedSourceFiles.First();
                    var spriteType = firstSourceFile.Settings.SpriteType ?? SpriteType.Parallel;
                    var spriteTextureFormat = firstSourceFile.Settings.SpriteTextureFormat ?? SpriteTextureFormat.Additive;

                    var sprite = CreateSpriteFromImages(frameImages, spriteType, spriteTextureFormat);
                    sprite.Save(outputSpritePath);

                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Log($"ERROR: Failed to build '{spriteName}': {ex.GetType().Name}: '{ex.Message}'.");
                return false;
            }
        }

        private static Sprite CreateSpriteFromIndexedSourceFiles(string spriteName, SpriteSourceFileInfo[] sourceFiles, Logger logger)
        {
            var indexedFrameImages = sourceFiles
                .Select(sourceFile => ImageFileIO.LoadIndexedImage(sourceFile.Path))
                .Select((indexedImage, i) => GetIndexedFrameImages(indexedImage, sourceFiles[i].Settings))
                .ToArray();
            var palette = indexedFrameImages.First()[0].Palette;

            var firstSourceFile = sourceFiles.First();
            var spriteType = firstSourceFile.Settings.SpriteType ?? SpriteType.Parallel;
            var spriteTextureFormat = firstSourceFile.Settings.SpriteTextureFormat ?? SpriteTextureFormat.Additive;

            var spriteWidth = indexedFrameImages.Max(frameImages => frameImages.Max(frameImage => frameImage.Width));
            var spriteHeight = indexedFrameImages.Max(frameImages => frameImages.Max(frameImage => frameImage.Height));
            var isAnimatedSprite = indexedFrameImages.Sum(frameImages => frameImages.Count()) > 1;

            var sprite = Sprite.CreateSprite(spriteType, spriteTextureFormat, spriteWidth, spriteHeight, palette);
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                var sourceFile = sourceFiles[i];
                var frameImages = indexedFrameImages[i];

                foreach (var frameImage in frameImages)
                {
                    sprite.Frames.Add(new Frame(
                        FrameType.Single,
                        -(frameImage.Width / 2) + (sourceFile.Settings.FrameOffset?.X ?? 0),
                        (frameImage.Height / 2) + (sourceFile.Settings.FrameOffset?.Y ?? 0),
                        (uint)frameImage.Width,
                        (uint)frameImage.Height,
                        frameImage.ImageData));
                }
            }
            return sprite;


            IndexedImage[] GetIndexedFrameImages(IndexedImage indexedImage, SpriteSettings settings)
            {
                if (settings.SpritesheetTileSize is null)
                    return new[] { indexedImage };
                else
                    return GetIndexedSpritesheetTiles(indexedImage, settings.SpritesheetTileSize.Value);
            }

            IndexedImage[] GetIndexedSpritesheetTiles(IndexedImage spritesheet, Size tileSize)
            {
                // Frames are taken from left to right, then from top to bottom.
                var frameImages = new List<IndexedImage>();
                for (int y = 0; y + tileSize.Height <= spritesheet.Height; y += tileSize.Height)
                {
                    for (int x = 0; x + tileSize.Width <= spritesheet.Width; x += tileSize.Width)
                    {
                        var frameImage = new IndexedImage(tileSize.Width, tileSize.Height, spritesheet.Palette);
                        for (int row = 0; row < tileSize.Height; row++)
                            Array.Copy(spritesheet.ImageData, (y + row) * spritesheet.Width + x, frameImage.ImageData, row * tileSize.Width, tileSize.Width);
                        frameImages.Add(frameImage);
                    }
                }
                return frameImages.ToArray();
            }
        }

        private static int RemoveOutputSprites(string directory, Logger logger)
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
                    logger.Log($"Failed to remove '{spriteFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            // Then recursively try removing sub-directories:
            foreach (var subDirectoryPath in Directory.EnumerateDirectories(directory))
                spritesRemoved += RemoveOutputSprites(subDirectoryPath, logger);

            try
            {
                // Finally, remove this directory, but only if it's now empty:
                if (!Directory.EnumerateFiles(directory).Any() && !Directory.EnumerateDirectories(directory).Any())
                    Directory.Delete(directory);

                logger.Log($"Removed sub-directory '{directory}'.");
            }
            catch (Exception ex)
            {
                logger.Log($"Failed to remove sub-directory '{directory}': {ex.GetType().Name}: '{ex.Message}'.");
            }

            return spritesRemoved;
        }

        private static Image<Rgba32>[] GetSpritesheetTiles(Image<Rgba32> spritesheet, Size tileSize)
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

        private static Sprite CreateSpriteFromImages(IList<FrameImage> frameImages, SpriteType spriteType, SpriteTextureFormat spriteTextureFormat)
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

        private static (Rgba32[] palette, Dictionary<Rgba32, int> colorIndexMappingCache) CreatePaletteAndColorIndexMappingCache(
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

        private static Func<Rgba32, bool> MakeTransparencyPredicate(FrameImage frameImage, bool isAlphaTest)
        {
            var transparencyThreshold = isAlphaTest ? Math.Clamp(frameImage.Settings.AlphaTestTransparencyThreshold ?? 128, 0, 255) : 0;
            if (frameImage.Settings.AlphaTestTransparencyColor is Rgba32 transparencyColor)
                return color => color.A < transparencyThreshold || (color.R == transparencyColor.R && color.G == transparencyColor.G && color.B == transparencyColor.B);

            return color => color.A < transparencyThreshold;
        }

        private static byte[] CreateFrameImageData(
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
                    return Dithering.None(imageFrame, getColorIndex);

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(imageFrame, palette, getColorIndex, spriteSettings.DitherScale ?? 0.75f, isTransparent);
            }
        }


        /// <summary>
        /// Returns all potential input files for the given directory.
        /// Bookkeeping files and directories are ignored.
        /// </summary>
        private static IEnumerable<string> GetInputFilePaths(string inputDirectory)
        {
            foreach (var path in Directory.EnumerateFiles(inputDirectory))
            {
                if (SpriteMakingSettings.IsConfigurationFile(path) || SpriteMakingHistory.IsHistoryFile(path))
                    continue;

                yield return path;
            }
        }

        private static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
