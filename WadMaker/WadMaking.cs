using Shared.FileFormats;
using Shared;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using WadMaker.Settings;
using Shared.FileSystem;
using FileInfo = Shared.FileSystem.FileInfo;
using Shared.FileFormats.Indexed;

namespace WadMaker
{
    public static class WadMaking
    {
        public static void MakeWad(string inputDirectory, string outputWadFilePath, bool doFullRebuild, bool includeSubDirectories, Logger logger)
        {
            if (File.Exists(inputDirectory))
                throw new InvalidUsageException("Unable to create or update wad file: the input must be a directory, not a file.");
            else if (!Directory.Exists(inputDirectory))
                throw new InvalidUsageException($"Unable to create or update wad file: the input directory '{inputDirectory}' does not exist.");

            if (Path.GetExtension(outputWadFilePath).ToLowerInvariant() != ".wad")
                throw new InvalidUsageException($"Unable to create or update wad file: the output must be a .wad file.");
            else if (Directory.Exists(outputWadFilePath))
                throw new InvalidUsageException($"The output must be a file, not a directory.");


            var stopwatch = Stopwatch.StartNew();

            // We can do an incremental build if we have information about the previous build operation, and if the output file matches the output of that build operation:
            var wadMakingHistory = WadMakingHistory.Load(inputDirectory);
            var doIncrementalUpdate = !doFullRebuild && File.Exists(outputWadFilePath) && wadMakingHistory != null && wadMakingHistory.OutputFile.HasMatchingFileHash(outputWadFilePath);

            var wad = doIncrementalUpdate ? LoadWad(outputWadFilePath, logger) : new Wad();
            var wadMakingSettings = WadMakingSettings.Load(inputDirectory);

            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);
            var isDecalsWad = Path.GetFileNameWithoutExtension(outputWadFilePath).ToLowerInvariant() == "decals";
            var existingTextureNames = wad.Textures.Select(texture => texture.Name.ToLowerInvariant()).ToHashSet();


            logger.Log($"{(doIncrementalUpdate ? "Updating existing" : "Creating new")} {(isDecalsWad ? "decals wad file" : "wad file")}.");

            // Gather input files:
            var textureSourceFileGroups = GetInputFilePaths(inputDirectory, includeSubDirectories)
                .Select(wadMakingSettings.GetTextureSourceFileInfo)
                .Where(file => file.Settings.Ignore != true && (ImageFileIO.CanLoad(file.Path) || !string.IsNullOrEmpty(file.Settings.Converter)))
                .GroupBy(file => WadMakingSettings.GetTextureName(file.Path))
                .ToArray();

            // Keep track of some statistics, to keep the user informed:
            var addedTexturesCount = 0;
            var updatedTexturesCount = 0;
            var removedTexturesCount = 0;
            var errorCount = 0;

            try
            {
                var successfulTextureInputs = new Dictionary<string, TextureSourceFileInfo[]>();

                // Create textures and add them to the wad file:
                foreach (var textureSourceFileGroup in textureSourceFileGroups)
                {
                    var textureName = textureSourceFileGroup.Key;
                    var textureSourceFiles = textureSourceFileGroup.ToArray();
                    var isExistingTexture = existingTextureNames.Contains(textureName);

                    // Log a warning and skip this texture if anything is wrong with the input files:
                    var isValid = VerifyTextureSourceFiles(textureName, textureSourceFiles, logger);
                    var hasError = false;
                    if (isValid)
                    {
                        // Can we skip this texture (when updating an existing wad file)?
                        if (doIncrementalUpdate && isExistingTexture)
                        {
                            if (!HasBeenModified(textureName, textureSourceFiles, wadMakingHistory))
                            {
                                successfulTextureInputs[textureName] = textureSourceFiles;

                                logger.Log($"- No changes detected for '{textureName}', skipping update.");
                                continue;
                            }
                        }


                        try
                        {
                            // Build the texture and add it to the wad file:
                            var texture = MakeTexture(textureName, textureSourceFiles, conversionOutputDirectory, isDecalsWad, logger);

                            if (doIncrementalUpdate && isExistingTexture)
                            {
                                // Update (replace) existing texture:
                                var index = wad.Textures.FindIndex(texture => texture.Name == textureName);
                                if (index != -1)
                                    wad.Textures[index] = texture;

                                successfulTextureInputs[textureName] = textureSourceFiles;
                                updatedTexturesCount += 1;

                                logger.Log($"- Updated texture '{textureName}' (from {string.Join(", ", textureSourceFiles.Select(file => file.Path))}).");
                            }
                            else
                            {
                                // Add new texture:
                                wad.Textures.Add(texture);

                                successfulTextureInputs[textureName] = textureSourceFiles;
                                addedTexturesCount += 1;

                                logger.Log($"- Added texture '{textureName}' (from {string.Join(", ", textureSourceFiles.Select(file => file.Path))}).");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Log($"- ERROR: failed to build '{textureName}': {ex.GetType().Name}: '{ex.Message}'.");
                            errorCount += 1;
                            hasError = true;
                        }
                    }

                    // If the texture was not built successfully, remove the existing texture if we're doing an incremental update:
                    if ((!isValid || hasError) && doIncrementalUpdate && isExistingTexture)
                    {
                        var index = wad.Textures.FindIndex(texture => texture.Name == textureName);
                        if (index != -1)
                            wad.Textures.RemoveAt(index);
                    }
                }

                // When doing an incremental update, check whether any textures should be removed:
                if (doIncrementalUpdate)
                {
                    var newTextureNames = textureSourceFileGroups.Select(group => group.Key).ToHashSet();
                    foreach (var textureName in existingTextureNames.Except(newTextureNames))
                    {
                        wad.Textures.Remove(wad.Textures.First(texture => texture.Name.ToLowerInvariant() == textureName));
                        removedTexturesCount += 1;
                        logger.Log($"- Removed texture '{textureName}'.");
                    }
                }

                // Save the wad file:
                CreateDirectory(Path.GetDirectoryName(outputWadFilePath));
                wad.Save(outputWadFilePath);

                // Also save information about this build operation, to enable future incremental updates:
                var newHistory = new WadMakingHistory(FileInfo.FromFile(outputWadFilePath), successfulTextureInputs);
                newHistory.Save(inputDirectory);
            }
            finally
            {
                // Clean up temporary conversion files:
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

            // Finally, log the statistics:
            if (doIncrementalUpdate)
                logger.Log($"Updated '{outputWadFilePath}' from '{inputDirectory}': added {addedTexturesCount}, updated {updatedTexturesCount} and removed {removedTexturesCount} textures, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
            else
                logger.Log($"Created '{outputWadFilePath}', with {addedTexturesCount} textures from '{inputDirectory}', in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");

            if (errorCount == 0)
                logger.Log("No errors.");
            else
                logger.Log($"{errorCount} errors.");
        }


        /// <summary>
        /// Returns all potential input files for the given directory (and sub-directories, if required).
        /// Bookkeeping files and directories are ignored.
        /// </summary>
        private static IEnumerable<string> GetInputFilePaths(string inputDirectory, bool includeSubDirectories)
        {
            foreach (var path in Directory.EnumerateFiles(inputDirectory))
            {
                if (WadMakingSettings.IsConfigurationFile(path) || WadMakingHistory.IsHistoryFile(path))
                    continue;

                yield return path;
            }

            if (includeSubDirectories)
            {
                foreach (var directory in Directory.EnumerateDirectories(inputDirectory))
                {
                    if (ExternalConversion.IsConversionOutputDirectory(directory))
                        continue;

                    foreach (var path in GetInputFilePaths(directory, includeSubDirectories))
                        yield return path;
                }
            }
        }

        /// <summary>
        /// Check wether the texture name is valid, whether all required files are provided, and whether there are any duplicate input files.
        /// </summary>
        private static bool VerifyTextureSourceFiles(string textureName, TextureSourceFileInfo[] textureSourceFiles, Logger logger)
        {
            // Does this texture have a valid name?
            if (!IsValidTextureName(textureName))
            {
                logger.Log($"- WARNING: '{textureName}' is not a valid texture name ({string.Join(", ", textureSourceFiles.Select(file => file.Path))}). Skipping file(s).");
                return false;
            }
            else if (textureName.Length > 15)
            {
                logger.Log($"- WARNING: The name '{textureName}' is too long ({string.Join(", ", textureSourceFiles.Select(file => file.Path))}). Skipping file(s).");
                return false;
            }

            // Is the main image missing (e.g. only mipmap files have been provided, but not a full-size image)?
            var normalSourceFiles = textureSourceFiles.Where(file => file.Settings.IsFullbrightMask != true).ToArray();
            var mainSourceFile = normalSourceFiles.FirstOrDefault(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main);
            if (mainSourceFile is null)
            {
                logger.Log($"- WARNING: missing main file for '{textureName}' ({string.Join(", ", normalSourceFiles.Select(file => file.Path))}). Skipping file(s).");
                return false;
            }

            // Do we have conflicting input files (e.g. "foo.png" and "foo.jpg", or multiple images for the same mipmap level)?
            if (normalSourceFiles.Length > 1)
            {
                var hasDuplicateMipmaps = normalSourceFiles
                    .GroupBy(file => file.Settings.MipmapLevel ?? MipmapLevel.Main)
                    .Any(mipmapGroup => mipmapGroup.Count() > 1);
                if (hasDuplicateMipmaps)
                {
                    logger.Log($"- WARNING: conflicting input files detected for '{textureName}' ({string.Join(", ", normalSourceFiles.Select(file => file.Path))}). Skipping files.");
                    return false;
                }
            }

            // Check (optional) fullbright mask images:
            if (TextureName.IsFullbright(textureName) && mainSourceFile.Settings.NoFullbright != true)
            {
                var fullbrightSourceFiles = textureSourceFiles.Where(file => file.Settings.IsFullbrightMask == true).ToArray();
                if (fullbrightSourceFiles.Any())
                {
                    // Is the main mask image missing?
                    if (!fullbrightSourceFiles.Any(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main))
                    {
                        logger.Log($"- WARNING: missing main fullbright mask file for '{textureName}' ({string.Join(", ", fullbrightSourceFiles.Select(file => file.Path))}). Skipping file(s).");
                        return false;
                    }

                    // Do we have duplicate fullbright mipmaps?
                    if (fullbrightSourceFiles.Length > 1)
                    {
                        var hasDuplicateFullbrightMipmaps = fullbrightSourceFiles
                            .GroupBy(file => file.Settings.MipmapLevel ?? MipmapLevel.Main)
                            .Any(mipmapGroup => mipmapGroup.Count() > 1);
                        if (hasDuplicateFullbrightMipmaps)
                        {
                            logger.Log($"- WARNING: conflicting input files detected for '{textureName}' ({string.Join(", ", fullbrightSourceFiles.Select(file => file.Path))}). Skipping files.");
                            return false;
                        }
                    }
                }
            }

            // Everything is looking good so far:
            return true;
        }

        // TODO: Really allow all characters in this range? Aren't there some characters that may cause trouble (in .map files, for example, such as commas, parenthesis, etc.?)
        private static bool IsValidTextureName(string name) => name.All(c => c > 0 && c < 256 && c != ' ');

        private static bool HasBeenModified(string textureName, TextureSourceFileInfo[] textureSourceFiles, WadMakingHistory? wadMakingHistory)
        {
            if (wadMakingHistory is null || !wadMakingHistory.TextureInputs.TryGetValue(textureName, out var previousSourceFiles))
                return true;

            if (textureSourceFiles.Length != previousSourceFiles.Length)
                return true;

            // TODO: Moving a texture folder will result in different paths... so maybe use relative paths instead?
            foreach (var sourceFile in textureSourceFiles)
            {
                var previousSourceFile = previousSourceFiles.FirstOrDefault(file => file.Path == sourceFile.Path);
                if (previousSourceFile is null)
                    return true;

                if (sourceFile.FileSize != previousSourceFile.FileSize || sourceFile.FileHash != previousSourceFile.FileHash || sourceFile.Settings != previousSourceFile.Settings)
                    return true;
            }
            return false;
        }

        private static Texture MakeTexture(string textureName, TextureSourceFileInfo[] sourceFiles, string conversionOutputDirectory, bool isDecalsWad, Logger logger)
        {
            // First gather all input files, converting any if necessary:
            var convertedSourceFiles = new TextureSourceFileInfo[sourceFiles.Length];
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                var sourceFile = sourceFiles[i];
                if (sourceFile.Settings.Converter is null)
                {
                    convertedSourceFiles[i] = sourceFile;
                }
                else
                {
                    if (sourceFile.Settings.ConverterArguments is null)
                        throw new InvalidUsageException($"Unable to convert '{sourceFile.Path}': missing converter arguments.");

                    var conversionOutputPath = Path.Combine(conversionOutputDirectory, textureName);
                    CreateDirectory(conversionOutputDirectory);

                    var outputFilePaths = ExternalConversion.ExecuteConversionCommand(sourceFile.Settings.Converter, sourceFile.Settings.ConverterArguments, sourceFile.Path, conversionOutputPath, logger);
                    if (outputFilePaths.Length < 1)
                        throw new IOException("Unable to find converter output file. An output file must have the same name as the input file (different extensions are ok).");

                    var supportedOutputFilePaths = outputFilePaths.Where(ImageFileIO.CanLoad).ToArray();
                    if (supportedOutputFilePaths.Length < 1)
                        throw new IOException("The converter did not produce a supported file type.");
                    else if (supportedOutputFilePaths.Length > 1)
                        throw new IOException("The converter produced multiple supported file types. Only one output file should be created.");

                    convertedSourceFiles[i] = new TextureSourceFileInfo(supportedOutputFilePaths[0], 0, new FileHash(), DateTimeOffset.UtcNow, sourceFile.Settings);
                }
            }

            // Then build the texture:
            var mainFileSettings = sourceFiles.Single(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main && file.Settings.IsFullbrightMask != true).Settings;
            switch (mainFileSettings.TextureType)
            {
                default:
                case TextureType.MipmapTexture: return CreateMipmapTextureFromSourceFiles(textureName, convertedSourceFiles, isDecalsWad, logger);
                case TextureType.SimpleTexture: return CreateSimpleTextureFromSourceFiles(textureName, convertedSourceFiles, logger);
                case TextureType.Font: throw new NotSupportedException("Font textures are not supported.");
            }
        }

        private static Texture CreateMipmapTextureFromSourceFiles(string textureName, TextureSourceFileInfo[] sourceFiles, bool isDecalsWad, Logger logger)
        {
            var normalSourceFiles = sourceFiles.Where(file => file.Settings.IsFullbrightMask != true).ToArray();
            var mainSourceFile = normalSourceFiles.Single(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main);

            // Do we need to preserve the source image's palette?
            if (mainSourceFile.Settings.PreservePalette == true && ImageFileIO.IsIndexed(mainSourceFile.Path))
                return CreateMipmapTextureFromIndexedSourceFiles(textureName, normalSourceFiles, isDecalsWad, logger);


            // Load the main image, and any mipmap images:
            using (var mainImage = ImageFileIO.LoadImage(mainSourceFile.Path))
            using (var mipmapImages = new DisposableList<Image<Rgba32>?>(Enumerable.Repeat<Image<Rgba32>?>(null, 3)))
            {
                foreach (var sourceFile in normalSourceFiles)
                {
                    var mipmapLevel = (int)(sourceFile.Settings.MipmapLevel ?? MipmapLevel.Main);
                    if (mipmapLevel > 0)
                        mipmapImages[mipmapLevel - 1] = ImageFileIO.LoadImage(sourceFile.Path);
                }

                VerifyMipmapTextureSizes(textureName, mainImage, mipmapImages);


                // Create the texture:
                if (isDecalsWad)
                {
                    return CreateDecalTexture(textureName, mainSourceFile.Settings, mainImage, mipmapImages, logger);
                }
                else if (TextureName.IsTransparent(textureName))
                {
                    return CreateTransparentTexture(textureName, mainSourceFile.Settings, mainImage, mipmapImages, logger);
                }
                else if (TextureName.IsWater(textureName))
                {
                    return CreateWaterTexture(textureName, mainSourceFile.Settings, mainImage, mipmapImages, logger);
                }
                else if (TextureName.IsFullbright(textureName) && mainSourceFile.Settings.NoFullbright != true)
                {
                    var fullbrightSourceFiles = sourceFiles.Where(file => file.Settings.IsFullbrightMask == true).ToArray();
                    var mainFullbrightFile = fullbrightSourceFiles.SingleOrDefault(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main);

                    // If any fullbright mask files are present, load them:
                    using (var mainFullbrightImage = mainFullbrightFile is not null ? ImageFileIO.LoadImage(mainFullbrightFile.Path) : null)
                    using (var fullbrightMipmapImages = new DisposableList<Image<Rgba32>?>(Enumerable.Repeat<Image<Rgba32>?>(null, 3)))
                    {
                        foreach (var sourceFile in fullbrightSourceFiles)
                        {
                            var mipmapLevel = (int)(sourceFile.Settings.MipmapLevel ?? MipmapLevel.Main);
                            if (mipmapLevel > 0)
                                fullbrightMipmapImages[mipmapLevel - 1] = ImageFileIO.LoadImage(sourceFile.Path);
                        }

                        if (mainFullbrightImage is not null)
                        {
                            // Verify fullbright mask image size:
                            if (mainFullbrightImage.Width != mainImage.Width || mainFullbrightImage.Height != mainImage.Height)
                                throw new InvalidDataException($"Fullbright mask for '{textureName}' is {mainFullbrightImage.Width}x{mainFullbrightImage.Height}, which does not match the main texture image: {mainImage.Width}x{mainImage.Height}.");

                            // Verify fullbright mipmap sizes:
                            for (int i = 1; i < fullbrightMipmapImages.Count; i++)
                            {
                                var fullbrightMipmapImage = fullbrightMipmapImages[i - 1];
                                if (fullbrightMipmapImage is not null && (fullbrightMipmapImage.Width != mainImage.Width >> i || fullbrightMipmapImage.Height != mainImage.Height >> i))
                                    throw new InvalidDataException($"Fullbright mipmap {i} for texture '{textureName}' is {fullbrightMipmapImage.Width}x{fullbrightMipmapImage.Height} but should be {mainImage.Width >> i}x{mainImage.Height >> i}.");
                            }
                        }

                        return CreateFullbrightTexture(textureName, mainSourceFile.Settings, mainImage, mipmapImages, mainFullbrightImage, fullbrightMipmapImages, logger);
                    }
                }
                else
                {
                    return CreateNormalTexture(textureName, mainSourceFile.Settings, mainImage, mipmapImages, logger);
                }
            }
        }

        private static Texture CreateMipmapTextureFromIndexedSourceFiles(string textureName, TextureSourceFileInfo[] sourceFiles, bool isDecalsWad, Logger logger)
        {
            var mainSourceFile = sourceFiles.Single(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main);
            var indexedMainImage = ImageFileIO.LoadIndexedImage(mainSourceFile.Path);
            var palette = indexedMainImage.Palette.Concat(Enumerable.Range(0, 256 - indexedMainImage.Palette.Length).Select(i => new Rgba32())).ToArray();

            var indexedMipmapImages = new IndexedImage?[3];
            foreach (var sourceFile in sourceFiles)
            {
                var mipmapLevel = (int)(sourceFile.Settings.MipmapLevel ?? MipmapLevel.Main);
                if (mipmapLevel > 0)
                {
                    if (!ImageFileIO.IsIndexed(sourceFile.Path))
                        throw new InvalidDataException($"Mipmap {mipmapLevel} for texture '{textureName}' is not in an indexed format.");

                    var indexedImage = ImageFileIO.LoadIndexedImage(sourceFile.Path);
                    if (!Enumerable.SequenceEqual(indexedImage.Palette, indexedMainImage.Palette))
                        throw new InvalidDataException($"Mipmap {mipmapLevel} for texture '{textureName}' has a different palette.");

                    indexedMipmapImages[mipmapLevel - 1] = indexedImage;
                }
            }

            VerifyMipmapTextureSizes(textureName, indexedMainImage, indexedMipmapImages);

            if (indexedMipmapImages.Any(image => image is null))
            {
                // Make palette adjustments for certain texture types, to ensure that mipmaps will be generated correctly:
                var resizePalette = palette.ToArray();
                if (isDecalsWad)
                {
                    var decalColor = resizePalette[255];
                    resizePalette = Enumerable.Range(0, 256)
                        .Select(i => new Rgba32(decalColor.R, decalColor.G, decalColor.B, (byte)i))
                        .ToArray();
                }
                else if (TextureName.IsTransparent(textureName))
                {
                    resizePalette[255] = new Rgba32(0, 0, 0, 0);
                }

                // Create a true-color copy of the main image, for resizing:
                var mainImage = new Image<Rgba32>(indexedMainImage.Width, indexedMainImage.Height);
                for (int y = 0; y < indexedMainImage.Height; y++)
                    for (int x = 0; x < indexedMainImage.Width; x++)
                        mainImage[x, y] = resizePalette[indexedMainImage[x, y]];

                var colorIndexMappingCache = new Dictionary<Rgba32, int>();
                var isAnimatedTexture = TextureName.IsAnimated(textureName);
                var transparencyThreshold = Math.Clamp(mainSourceFile.Settings.TransparencyThreshold ?? 128, 0, 255);
                Func<Rgba32, bool> isTransparentPredicate = color => color.A < transparencyThreshold;

                for (int i = 0; i < indexedMipmapImages.Length; i++)
                {
                    if (indexedMipmapImages[i] is not null)
                        continue;

                    var mipmapImage = mainImage.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1)));
                    var indexedMipmapData = isDecalsWad ? Dithering.None(mipmapImage, color => color.A) :
                        CreateTextureData(mipmapImage, palette, colorIndexMappingCache, mainSourceFile.Settings, isTransparentPredicate, disableDithering: isAnimatedTexture);
                    indexedMipmapImages[i] = new IndexedImage(indexedMipmapData, mipmapImage.Width, mipmapImage.Height, palette);
                }
            }

            var mipmapTextureData = indexedMipmapImages
                .Select(image => image!.ImageData)
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: textureName,
                width: indexedMainImage.Width,
                height: indexedMainImage.Height,
                imageData: indexedMainImage.ImageData,
                palette: palette,
                mipmap1Data: mipmapTextureData[0],
                mipmap2Data: mipmapTextureData[1],
                mipmap3Data: mipmapTextureData[2]);
        }

        private static Texture CreateDecalTexture(string textureName, TextureSettings textureSettings, Image<Rgba32> mainImage, IReadOnlyList<Image<Rgba32>?> mipmapImages, Logger logger)
        {
            // Create any missing mipmaps (this does not affect the palette, so it can be done up-front):
            var mipmaps = mipmapImages
                .Select((image, i) => image ?? mainImage.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1))))
                .ToArray();

            // The last palette color determines the color of the decal. All other colors are irrelevant - palette indexes are treated as alpha values instead.
            var decalColor = textureSettings.DecalColor ?? ColorQuantization.GetAverageColor(ColorQuantization.GetColorHistogram(mipmaps, color => color.A == 0));
            var palette = Enumerable.Range(0, 255)
                .Select(i => new Rgba32((byte)i, (byte)i, (byte)i))
                .Append(decalColor)
                .ToArray();

            var mainTextureData = CreateDecalTextureData(mainImage);
            var mipmapTextureData = mipmaps
                .Select(CreateDecalTextureData!)
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: textureName,
                width: mainImage.Width,
                height: mainImage.Height,
                imageData: mainTextureData,
                palette: palette,
                mipmap1Data: mipmapTextureData[0],
                mipmap2Data: mipmapTextureData[1],
                mipmap3Data: mipmapTextureData[2]);


            byte[] CreateDecalTextureData(Image<Rgba32> image)
            {
                var mode = textureSettings.DecalTransparencySource ?? DecalTransparencySource.AlphaChannel;
                if (mode == DecalTransparencySource.AlphaChannel)
                    return Dithering.None(image, color => color.A);
                else
                    return Dithering.None(image, color => (color.R + color.G + color.B) / 3);
            }
        }

        private static Texture CreateTransparentTexture(string textureName, TextureSettings textureSettings, Image<Rgba32> mainImage, IReadOnlyList<Image<Rgba32>?> mipmapImages, Logger logger)
        {
            // Any pixel with an alpha value below the configured threshold will be treated as transparent.
            // It's also possible to treat a specific color as transparent:
            var transparencyThreshold = Math.Clamp(textureSettings.TransparencyThreshold ?? 128, 0, 255);
            Func<Rgba32, bool> isTransparentPredicate;
            if (textureSettings.TransparencyColor != null)
            {
                var transparencyColor = textureSettings.TransparencyColor.Value;
                isTransparentPredicate = color => color.A < transparencyThreshold || (color.R == transparencyColor.R && color.G == transparencyColor.G && color.B == transparencyColor.B);
            }
            else
            {
                isTransparentPredicate = color => color.A < transparencyThreshold;
            }

            // Create the palette, and make sure we end up with a 256-color palette (some tools can't handle smaller palettes):
            var maxColors = 255;
            var images = new[] { mainImage }.Concat(mipmapImages.Where(image => image != null));
            var colorHistogram = ColorQuantization.GetColorHistogram(images!, isTransparentPredicate);
            var colorClusters = ColorQuantization.GetColorClusters(colorHistogram, maxColors);
            if (colorClusters.Length < maxColors)
            {
                colorClusters = colorClusters
                    .Concat(Enumerable.Range(0, maxColors - colorClusters.Length).Select(i => (new Rgba32(), new[] { new Rgba32() })))
                    .ToArray();
            }

            // The last palette slot is reserved for transparent areas:
            var colorKey = new Rgba32(0, 0, 255);
            colorClusters = colorClusters
                .Append((colorKey, new[] { colorKey }))         // Slot 255: used for transparent pixels
                .ToArray();

            // Create the actual palette, and a color index lookup cache:
            var palette = colorClusters.Select(cluster => cluster.averageColor).ToArray();
            var colorIndexMappingCache = new Dictionary<Rgba32, int>();
            for (int i = 0; i < colorClusters.Length; i++)
            {
                (_, var colors) = colorClusters[i];
                foreach (var color in colors)
                    colorIndexMappingCache[color] = i;
            }

            // Create any missing mipmaps:
            var mipmaps = mipmapImages
                .Select((image, i) => image ?? mainImage.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1))))
                .ToArray();

            // Create texture data:
            var mainTextureData = CreateTextureData(mainImage, palette, colorIndexMappingCache, textureSettings, isTransparentPredicate, false);
            var mipmapTextureData = mipmaps
                .Select(image => CreateTextureData(image, palette, colorIndexMappingCache, textureSettings, isTransparentPredicate, false))
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: textureName,
                width: mainImage.Width,
                height: mainImage.Height,
                imageData: mainTextureData,
                palette: palette,
                mipmap1Data: mipmapTextureData[0],
                mipmap2Data: mipmapTextureData[1],
                mipmap3Data: mipmapTextureData[2]);
        }

        private static Texture CreateWaterTexture(string textureName, TextureSettings textureSettings, Image<Rgba32> mainImage, IReadOnlyList<Image<Rgba32>?> mipmapImages, Logger logger)
        {
            // Create the palette, and make sure we end up with a 256-color palette (some tools can't handle smaller palettes):
            var maxColors = 254;
            var images = new[] { mainImage }.Concat(mipmapImages.Where(image => image != null));
            var colorHistogram = ColorQuantization.GetColorHistogram(images!, color => false);
            var colorClusters = ColorQuantization.GetColorClusters(colorHistogram, maxColors);
            if (colorClusters.Length < maxColors)
            {
                colorClusters = colorClusters
                    .Concat(Enumerable.Range(0, maxColors - colorClusters.Length).Select(i => (new Rgba32(), new[] { new Rgba32() })))
                    .ToArray();
            }

            // The 3rd and 4th palette slots are reserved for water fog color and intensity:
            var fogColor = textureSettings.WaterFogColor ?? ColorQuantization.GetAverageColor(colorHistogram);
            var fogIntensity = new Rgba32((byte)Math.Clamp(textureSettings.WaterFogColor?.A ?? (int)((1f - GetBrightness(fogColor)) * 255), 0, 255), 0, 0);
            colorClusters = colorClusters.Take(3)
                .Append((fogColor, new[] { fogColor }))         // Slot 3: water fog color
                .Append((fogIntensity, new[] { fogIntensity })) // Slot 4: fog intensity (stored in red channel)
                .Concat(colorClusters.Skip(3))
                .ToArray();

            // Create the actual palette, and a color index lookup cache:
            var palette = colorClusters.Select(cluster => cluster.averageColor).ToArray();
            var colorIndexMappingCache = new Dictionary<Rgba32, int>();
            for (int i = 0; i < colorClusters.Length; i++)
            {
                (_, var colors) = colorClusters[i];
                foreach (var color in colors)
                    colorIndexMappingCache[color] = i;
            }

            // Create any missing mipmaps:
            var mipmaps = mipmapImages
                .Select((image, i) => image ?? mainImage.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1))))
                .ToArray();

            // Create texture data:
            var mainTextureData = CreateTextureData(mainImage, palette, colorIndexMappingCache, textureSettings, color => false, false);
            var mipmapTextureData = mipmaps
                .Select(image => CreateTextureData(image, palette, colorIndexMappingCache, textureSettings, color => false, false))
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: textureName,
                width: mainImage.Width,
                height: mainImage.Height,
                imageData: mainTextureData,
                palette: palette,
                mipmap1Data: mipmapTextureData[0],
                mipmap2Data: mipmapTextureData[1],
                mipmap3Data: mipmapTextureData[2]);
        }

        private static Texture CreateNormalTexture(string textureName, TextureSettings textureSettings, Image<Rgba32> mainImage, IReadOnlyList<Image<Rgba32>?> mipmapImages, Logger logger)
        {
            // Create the palette, and make sure we end up with a 256-color palette (some tools can't handle smaller palettes):
            var maxColors = 256;
            var images = new[] { mainImage }.Concat(mipmapImages.Where(image => image != null));
            var colorHistogram = ColorQuantization.GetColorHistogram(images!, color => false);
            var colorClusters = ColorQuantization.GetColorClusters(colorHistogram, maxColors);
            if (colorClusters.Length < maxColors)
            {
                colorClusters = colorClusters
                    .Concat(Enumerable.Range(0, maxColors - colorClusters.Length).Select(i => (new Rgba32(), new[] { new Rgba32() })))
                    .ToArray();
            }

            // Create the actual palette, and a color index lookup cache:
            var palette = colorClusters.Select(cluster => cluster.averageColor).ToArray();
            var colorIndexMappingCache = new Dictionary<Rgba32, int>();
            for (int i = 0; i < colorClusters.Length; i++)
            {
                (_, var colors) = colorClusters[i];
                foreach (var color in colors)
                    colorIndexMappingCache[color] = i;
            }

            // Create any missing mipmaps:
            var mipmaps = mipmapImages
                .Select((image, i) => image ?? mainImage.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1))))
                .ToArray();

            // Create texture data (disable dithering for animated textures in an attempt to reduce 'flickering' between frames):
            var isAnimatedTexture = TextureName.IsAnimated(textureName);
            var mainTextureData = CreateTextureData(mainImage, palette, colorIndexMappingCache, textureSettings, color => false, disableDithering: isAnimatedTexture);
            var mipmapTextureData = mipmaps
                .Select(image => CreateTextureData(image, palette, colorIndexMappingCache, textureSettings, color => false, disableDithering: isAnimatedTexture))
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: textureName,
                width: mainImage.Width,
                height: mainImage.Height,
                imageData: mainTextureData,
                palette: palette,
                mipmap1Data: mipmapTextureData[0],
                mipmap2Data: mipmapTextureData[1],
                mipmap3Data: mipmapTextureData[2]);
        }

        private static Texture CreateFullbrightTexture(
            string textureName,
            TextureSettings textureSettings,
            Image<Rgba32> mainImage,
            IReadOnlyList<Image<Rgba32>?> mipmapImages,
            Image<Rgba32>? fullbrightMaskImage,
            IReadOnlyList<Image<Rgba32>?> fullbrightMipmapImages,
            Logger logger)
        {
            // With fullbright textures, the last 32 colors are used for full-bright pixels:
            var maxFullbrightColors = 32;
            var maxNormalColors = 256 - maxFullbrightColors;
            var fullbrightAlphaThreshold = textureSettings.FullbrightAlphaThreshold ?? 128;

            var normalImages = new[] { mainImage }.Concat(mipmapImages).ToArray();
            var fullbrightImages = new[] { fullbrightMaskImage }.Concat(fullbrightMipmapImages).ToArray();

            var normalColorHistogram = new Dictionary<Rgba32, int>();
            var fullbrightColorHistogram = new Dictionary<Rgba32, int>();
            for (int i = 0; i < normalImages.Length; i++)
            {
                var normalImage = normalImages[i];
                var fullbrightImage = fullbrightImages[i];

                if (normalImage is not null)
                {
                    if (fullbrightImage is not null)
                        ColorQuantization.UpdateColorHistogram(normalColorHistogram, normalImage.Frames[0], (x, y, color) => fullbrightImage[x, y].A >= fullbrightAlphaThreshold);
                    else
                        ColorQuantization.UpdateColorHistogram(normalColorHistogram, normalImage.Frames[0], color => false);
                }

                if (fullbrightImage is not null)
                    ColorQuantization.UpdateColorHistogram(fullbrightColorHistogram, fullbrightImage.Frames[0], color => color.A < fullbrightAlphaThreshold);
            }

            // Create a color index lookup cache for normal pixels:
            var normalColorClusters = ColorQuantization.GetColorClusters(normalColorHistogram, maxNormalColors);
            var normalColorIndexMappingCache = new Dictionary<Rgba32, int>();
            for (int i = 0; i < normalColorClusters.Length; i++)
            {
                (_, var colors) = normalColorClusters[i];
                foreach (var color in colors)
                    normalColorIndexMappingCache[color] = i;
            }

            // And a separate color index lookup cache for fullbright pixels:
            var fullbrightColorClusters = ColorQuantization.GetColorClusters(fullbrightColorHistogram, maxFullbrightColors);
            var fullbrightColorIndexMappingCache = new Dictionary<Rgba32, int>();
            for (int i = 0; i < fullbrightColorClusters.Length; i++)
            {
                (_, var colors) = fullbrightColorClusters[i];
                foreach (var color in colors)
                    fullbrightColorIndexMappingCache[color] = i;
            }

            // Create the palette, and make sure we end up with a 256-color palette (some tools can't handle smaller palettes):
            var palette = normalColorClusters.Select(cluster => cluster.averageColor)
                .Concat(Enumerable.Repeat(new Rgba32(), maxNormalColors - normalColorClusters.Length))
                .Concat(fullbrightColorClusters.Select(cluster => cluster.averageColor))
                .Concat(Enumerable.Repeat(new Rgba32(), maxFullbrightColors - fullbrightColorClusters.Length))
                .ToArray();

            // Create any missing mipmaps (fullbright mipmaps only need to be created if there is a fullbright mask image):
            var normalMipmaps = mipmapImages
                .Select((image, i) => image ?? mainImage.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1))))
                .ToArray();
            var fullbrightMipmaps = fullbrightMipmapImages
                .Select((image, i) => image ?? fullbrightMaskImage?.Clone(context => context.Resize(mainImage.Width >> (i + 1), mainImage.Height >> (i + 1))))
                .ToArray();

            // Create texture data:
            var isAnimatedTexture = TextureName.IsAnimated(textureName);
            var mainTextureData = CreateFullbrightTextureData(
                mainImage,
                fullbrightMaskImage,
                palette,
                normalColorIndexMappingCache,
                fullbrightColorIndexMappingCache,
                textureSettings,
                maxNormalColors,
                fullbrightAlphaThreshold,
                disableDithering: isAnimatedTexture);

            var mipmapTextureData = normalMipmaps
                .Select((image, i) => CreateFullbrightTextureData(
                    image,
                    fullbrightMipmaps[i],
                    palette,
                    normalColorIndexMappingCache,
                    fullbrightColorIndexMappingCache,
                    textureSettings,
                    maxNormalColors,
                    fullbrightAlphaThreshold,
                    disableDithering: isAnimatedTexture))
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: textureName,
                width: mainImage.Width,
                height: mainImage.Height,
                imageData: mainTextureData,
                palette: palette,
                mipmap1Data: mipmapTextureData[0],
                mipmap2Data: mipmapTextureData[1],
                mipmap3Data: mipmapTextureData[2]);
        }

        private static Texture CreateSimpleTextureFromSourceFiles(string textureName, TextureSourceFileInfo[] sourceFiles, Logger logger)
        {
            if (sourceFiles.Length > 1)
                logger.Log($"- WARNING: Skipping mipmap files for simple texture (qpic) '{textureName}'.");


            var mainFile = sourceFiles.Single(file => (file.Settings.MipmapLevel ?? MipmapLevel.Main) == MipmapLevel.Main && file.Settings.IsFullbrightMask != true);

            // Do we need to preserve the source image's palette?
            if (mainFile.Settings.PreservePalette == true && ImageFileIO.IsIndexed(mainFile.Path))
            {
                var indexedImage = ImageFileIO.LoadIndexedImage(mainFile.Path);
                var palette = indexedImage.Palette.Concat(Enumerable.Range(0, 256 - indexedImage.Palette.Length).Select(i => new Rgba32())).ToArray();

                return Texture.CreateSimpleTexture(
                    name: textureName,
                    width: indexedImage.Width,
                    height: indexedImage.Height,
                    imageData: indexedImage.ImageData,
                    palette: palette);
            }


            using (var image = ImageFileIO.LoadImage(mainFile.Path))
            {
                var colorHistogram = ColorQuantization.GetColorHistogram(new[] { image }, color => false);
                var maxColors = 256;
                var colorClusters = ColorQuantization.GetColorClusters(colorHistogram, maxColors);

                // Always make sure we've got a 256-color palette (some tools can't handle smaller palettes):
                if (colorClusters.Length < maxColors)
                {
                    colorClusters = colorClusters
                        .Concat(Enumerable.Range(0, maxColors - colorClusters.Length).Select(i => (new Rgba32(), new[] { new Rgba32() })))
                        .ToArray();
                }

                // Create the actual palette, and a color index lookup cache:
                var palette = colorClusters
                    .Select(cluster => cluster.Item1)
                    .ToArray();
                var colorIndexMappingCache = new Dictionary<Rgba32, int>();
                for (int i = 0; i < colorClusters.Length; i++)
                {
                    (_, var colors) = colorClusters[i];
                    foreach (var color in colors)
                        colorIndexMappingCache[color] = i;
                }

                // Create texture data:
                var textureData = CreateTextureData(image, palette, colorIndexMappingCache, mainFile.Settings, color => false, disableDithering: false);

                return Texture.CreateSimpleTexture(
                    name: textureName,
                    width: image.Width,
                    height: image.Height,
                    imageData: textureData,
                    palette: palette);
            }
        }



        private static void VerifyMipmapTextureSizes(string textureName, Image<Rgba32> mainImage, IReadOnlyList<Image<Rgba32>?> mipmapImages)
            => VerifyMipmapTextureSizes(textureName, mainImage, mipmapImages, image => (image.Width, image.Height));

        private static void VerifyMipmapTextureSizes(string textureName, IndexedImage mainImage, IReadOnlyList<IndexedImage?> mipmapImages)
            => VerifyMipmapTextureSizes(textureName, mainImage, mipmapImages, image => (image.Width, image.Height));

        private static void VerifyMipmapTextureSizes<T>(string textureName, T mainImage, IReadOnlyList<T?> mipmapImages, Func<T, (int, int)> getSize)
        {
            // Verify main image size:
            (var width, var height) = getSize(mainImage);
            if (width % 16 != 0 || height % 16 != 0)
                throw new InvalidDataException($"Texture '{textureName}' is {width}x{height}. Both width and height must be a multiple of 16.");

            // Verify mipmap sizes:
            for (int i = 1; i < mipmapImages.Count; i++)
            {
                var mipmapImage = mipmapImages[i - 1];
                if (mipmapImage is null)
                    continue;

                (var mipmapWidth, var mipmapHeight) = getSize(mipmapImage);
                if (mipmapWidth != width >> i || mipmapHeight != height >> i)
                    throw new InvalidDataException($"Mipmap {i} for texture '{textureName}' is {mipmapWidth}x{mipmapHeight} but should be {width >> i}x{height >> i}.");
            }
        }


        private static int GetBrightness(Rgba32 color) => (int)(color.R * 0.21 + color.G * 0.72 + color.B * 0.07);

        // TODO: Disable dithering for animated textures again? It doesn't actually remove all flickering, because different frames can still have different palettes...!
        private static byte[] CreateTextureData(
            Image<Rgba32> image,
            Rgba32[] palette,
            IDictionary<Rgba32, int> colorIndexMappingCache,
            TextureSettings textureSettings,
            Func<Rgba32, bool> isTransparent,
            bool disableDithering)
        {
            var getColorIndex = ColorQuantization.CreateColorIndexLookup(palette, colorIndexMappingCache, isTransparent);
            var ditheringAlgorithm = textureSettings.DitheringAlgorithm ?? (disableDithering ? DitheringAlgorithm.None : DitheringAlgorithm.FloydSteinberg);
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None:
                    return Dithering.None(image, getColorIndex);

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(image, palette, getColorIndex, textureSettings.DitherScale ?? 0.75f, isTransparent);
            }
        }

        private static byte[] CreateFullbrightTextureData(
            Image<Rgba32> image,
            Image<Rgba32>? fullbrightImage,
            Rgba32[] palette,
            IDictionary<Rgba32, int> normalColorIndexMappingCache,
            IDictionary<Rgba32, int> fullbrightColorIndexMappingCache,
            TextureSettings textureSettings,
            int maxNormalColors,
            int fullbrightAlphaThreshold,
            bool disableDithering)
        {
            var ditheringAlgorithm = textureSettings.DitheringAlgorithm ?? (disableDithering ? DitheringAlgorithm.None : DitheringAlgorithm.FloydSteinberg);

            // First create texture data for normal pixels:
            var normalPalette = palette.Take(maxNormalColors).ToArray();
            var getNormalColorIndex = ColorQuantization.CreateColorIndexLookup(normalPalette, normalColorIndexMappingCache, color => false);
            byte[] textureData;
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None:
                    textureData = Dithering.None(image, getNormalColorIndex);
                    break;

                case DitheringAlgorithm.FloydSteinberg:
                    textureData = Dithering.FloydSteinberg(image, normalPalette, getNormalColorIndex, textureSettings.DitherScale ?? 0.75f);
                    break;
            }

            if (fullbrightImage is not null)
            {
                // If a fullbright mask is provided, create texture data for fullbright pixels:
                var fullbrightPalette = palette.Skip(maxNormalColors).ToArray();
                var getFullbrightColorIndex = ColorQuantization.CreateColorIndexLookup(fullbrightPalette, fullbrightColorIndexMappingCache, color => color.A < fullbrightAlphaThreshold);

                byte[] fullbrightTextureData;
                switch (ditheringAlgorithm)
                {
                    default:
                    case DitheringAlgorithm.None:
                        fullbrightTextureData = Dithering.None(fullbrightImage, getFullbrightColorIndex);
                        break;

                    case DitheringAlgorithm.FloydSteinberg:
                        fullbrightTextureData = Dithering.FloydSteinberg(fullbrightImage, fullbrightPalette, getFullbrightColorIndex, textureSettings.DitherScale ?? 0.75f, color => color.A < fullbrightAlphaThreshold);
                        break;
                }

                // Merge the fullbright pixel data into the normal texture data:
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (fullbrightImage[x, y].A >= fullbrightAlphaThreshold)
                        {
                            textureData[y * image.Width + x] = (byte)(maxNormalColors + fullbrightTextureData[y * image.Width + x]);
                        }
                    }
                }
            }

            return textureData;
        }


        private static Wad LoadWad(string filePath, Logger logger)
        {
            logger.Log($"Loading wad file: '{filePath}'.");
            return Wad.Load(filePath, (index, name, exception) => logger.Log($"- Failed to load texture #{index} ('{name}'): {exception.GetType().Name}: '{exception.Message}'."));
        }

        private static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
