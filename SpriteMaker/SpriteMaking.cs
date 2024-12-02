using Shared.FileFormats;
using Shared.Sprites;
using Shared;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Security.Cryptography;

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
            var imagePaths = Directory.EnumerateFiles(inputDirectory)
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .Where(path => SpriteMakingSettings.GetSpriteName(path) == spriteName)
                .Where(path =>
                {
                    var settings = spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings;
                    return settings.Ignore != true && (ImageReading.IsSupported(path) || settings.Converter != null);
                })
                .ToArray();

            var conversionOutputDirectory = Path.Combine(inputDirectory, Guid.NewGuid().ToString());
            try
            {
                var success = MakeSprite(spriteName, imagePaths, outputPath, spriteMakingSettings, conversionOutputDirectory, true, logger);
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

            logger.Log($"Created '{outputPath}' (from '{imagePaths.First()}'{(imagePaths.Length > 1 ? $" + {imagePaths.Length - 1} more files" : "")}) in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        static (int spritesAdded, int spritesUpdated, int spritesRemoved) MakeSpritesFromImagesDirectory(
            string inputDirectory,
            string outputDirectory,
            bool fullRebuild,
            bool includeSubDirectories,
            bool enableSubDirectoryRemoving,
            Logger logger)
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
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .Where(path =>
                {
                    var settings = spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings;
                    return settings.Ignore != true && (ImageReading.IsSupported(path) || settings.Converter != null);
                })
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
                        logger,
                        currentFileHashes);

                    if (success)
                    {
                        var inputImageCount = imagePathsGroup.Count();
                        if (isExistingSprite)
                        {
                            spritesUpdated += 1;
                            logger.Log($"- Updated sprite '{outputSpritePath}' (from '{imagePathsGroup.First()}'{(inputImageCount > 1 ? $" + {inputImageCount - 1} more files" : "")}).");
                        }
                        else
                        {
                            spritesAdded += 1;
                            logger.Log($"- Added sprite '{outputSpritePath}' (from '{imagePathsGroup.First()}'{(inputImageCount > 1 ? $" + {inputImageCount - 1} more files" : "")}).");
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
                        fullRebuild,
                        includeSubDirectories,
                        enableSubDirectoryRemoving,
                        logger);

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
                            spritesRemoved += RemoveOutputSprites(Path.Combine(outputDirectory, subDirectoryName), logger);
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
            Logger logger,
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
                        if (filenameSettings.Type != null) spriteSettings.settings.SpriteType = filenameSettings.Type;
                        if (filenameSettings.TextureFormat != null) spriteSettings.settings.SpriteTextureFormat = filenameSettings.TextureFormat;
                        if (filenameSettings.FrameOffset != null) spriteSettings.settings.FrameOffset = filenameSettings.FrameOffset;

                        return (path, isSupportedFileType, filenameSettings, spriteSettings);
                    })
                    .OrderBy(file => file.filenameSettings.FrameNumber)
                    .ToArray();

                if (imagePathsAndSettings.Any(file => !file.isSupportedFileType && file.spriteSettings.settings.ConverterArguments == null))
                {
                    logger.Log($"WARNING: some input files for '{spriteName}' are missing converter arguments. Skipping sprite.");
                    return false;
                }
                else if (imagePaths.Count() > 1 && imagePathsAndSettings.Any(file => file.filenameSettings.FrameNumber == null))
                {
                    logger.Log($"WARNING: not all input files for '{spriteName}' contain a frame number ({string.Join(", ", imagePaths)}). Skipping sprite.");
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

                            var outputFilePaths = ExternalConversion.ExecuteConversionCommand(spriteSettings.Converter, spriteSettings.ConverterArguments, file.path, initialImageFilePath, logger);
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
                logger.Log($"ERROR: Failed to build '{spriteName}': {ex.GetType().Name}: '{ex.Message}'.");
                return false;
            }
        }

        static int RemoveOutputSprites(string directory, Logger logger)
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
            var transparencyThreshold = isAlphaTest ? Math.Clamp(frameImage.Settings.AlphaTestTransparencyThreshold ?? 128, 0, 255) : 0;
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


        static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }

        static byte[] GetFileHash(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha256 = SHA256.Create())
                return sha256.ComputeHash(file);
        }

        static bool IsEqualHash(byte[]? hash1, byte[]? hash2) => hash1 != null && hash2 != null && Enumerable.SequenceEqual(hash1, hash2);
    }
}
