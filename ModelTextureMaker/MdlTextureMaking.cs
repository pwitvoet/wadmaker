using FileInfo = Shared.FileSystem.FileInfo;
using ModelTextureMaker.Settings;
using Shared;
using Shared.FileFormats;
using System.Diagnostics;
using Shared.FileSystem;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Text.RegularExpressions;
using Shared.FileFormats.Indexed;

namespace ModelTextureMaker
{
    public static class MdlTextureMaking
    {
        public static void MakeTextures(string inputDirectory, string outputDirectory, bool fullRebuild, bool includeSubDirectories, bool enableSubDirectoryRemoving, Logger logger)
        {
            if (File.Exists(inputDirectory))
                throw new InvalidUsageException("Unable to create or update textures: the input must be a directory, not a file.");
            else if (!Directory.Exists(inputDirectory))
                throw new InvalidUsageException($"Unable to create or update textures: the input directory '{inputDirectory}' does not exist.");


            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Creating model textures from '{inputDirectory}' and saving it to '{outputDirectory}'.");

            (var texturesAdded, var texturesUpdated, var texturesRemoved) = MakeTexturesFromImagesDirectory(inputDirectory, outputDirectory, fullRebuild, includeSubDirectories, enableSubDirectoryRemoving, logger);

            logger.Log($"Updated '{outputDirectory}' from '{inputDirectory}': added {texturesAdded}, updated {texturesUpdated} and removed {texturesRemoved} textures, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        private static (int texturesAdded, int texturesUpdated, int texturesRemoved) MakeTexturesFromImagesDirectory(
            string inputDirectory,
            string outputDirectory,
            bool doFullRebuild,
            bool includeSubDirectories,
            bool enableSubDirectoryRemoving,
            Logger logger)
        {
            // We can do an incremental build if we have information about the previous build operation, and if the output directory already exists:
            var textureMakingHistory = MdlTextureMakingHistory.Load(inputDirectory);
            var doIncrementalUpdate = !doFullRebuild && Directory.Exists(outputDirectory) && textureMakingHistory != null;

            var textureMakingSettings = MdlTextureMakingSettings.Load(inputDirectory);
            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);

            // Gather input files:
            var textureSourceFileGroups = GetInputFilePaths(inputDirectory)
                .Select(textureMakingSettings.GetTextureSourceFileInfo)
                .Where(file => file.Settings.Ignore != true && (ImageFileIO.CanLoad(file.Path) || !string.IsNullOrEmpty(file.Settings.Converter)))
                .GroupBy(file => MdlTextureMakingSettings.GetTextureName(file.Path))
                .ToArray();

            // Keep track of some statistics, to keep the user informed:
            var texturesAdded = 0;
            var texturesUpdated = 0;
            var texturesRemoved = 0;

            Util.CreateDirectory(outputDirectory);

            var successfulTextureInputs = new Dictionary<string, MdlTextureMakingHistory.MdlTextureHistory>();

            try
            {
                // Create textures and save them to the output directory:
                foreach (var textureSourceFileGroup in textureSourceFileGroups)
                {
                    try
                    {
                        var textureName = textureSourceFileGroup.Key;
                        var textureSourceFiles = textureSourceFileGroup.ToArray();

                        var outputTexturePath = Path.Combine(outputDirectory, GetTextureFileName(textureName, textureSourceFileGroup) + ".bmp");
                        var isExistingTexture = File.Exists(outputTexturePath);

                        // Can we skip this texture (when updating an existing output directory)?
                        if (doIncrementalUpdate && isExistingTexture && !HasBeenModified(textureName, outputTexturePath, textureSourceFiles, textureMakingHistory))
                        {
                            var outputFile = FileInfo.FromFile(outputTexturePath);
                            successfulTextureInputs[textureName] = new MdlTextureMakingHistory.MdlTextureHistory(outputFile, textureSourceFiles);

                            logger.Log($"- No changes detected for '{textureName}', skipping update.");
                            continue;
                        }

                        var texture = MakeTexture(textureName, textureSourceFiles, textureMakingSettings, conversionOutputDirectory, logger);
                        if (texture != null)
                        {
                            var textureImage = new IndexedImage(texture.ImageData, texture.Width, texture.Height, texture.Palette);
                            ImageFileIO.SaveIndexedImage(textureImage, outputTexturePath, ImageFormat.Bmp);

                            var outputFile = FileInfo.FromFile(outputTexturePath);
                            successfulTextureInputs[textureName] = new MdlTextureMakingHistory.MdlTextureHistory(outputFile, textureSourceFiles);

                            if (isExistingTexture)
                            {
                                texturesUpdated += 1;
                                logger.Log($"- Updated texture '{outputTexturePath}' (from '{textureSourceFiles.First().Path}'{(textureSourceFiles.Length > 1 ? $" + {textureSourceFiles.Length - 1} more files" : "")}).");
                            }
                            else
                            {
                                texturesAdded += 1;
                                logger.Log($"- Added texture '{outputTexturePath}' (from '{textureSourceFiles.First().Path}'{(textureSourceFiles.Length > 1 ? $" + {textureSourceFiles.Length - 1} more files" : "")}).");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"WARNING: Failed to make texture '{textureSourceFileGroup.Key}': {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }

                // Remove textures whose source images have been removed:
                if (textureMakingHistory != null)
                {
                    var oldTextureNames = textureMakingHistory.Textures
                        .Select(kv => kv.Key)
                        .ToHashSet();
                    var newTextureNames = textureSourceFileGroups
                        .Select(group => GetTextureFileName(group.Key, group))
                        .ToHashSet();

                    foreach (var textureName in oldTextureNames)
                    {
                        if (!newTextureNames.Contains(textureName))
                        {
                            var textureFilePath = Path.Combine(outputDirectory, textureName + ".bmp");
                            try
                            {
                                if (File.Exists(textureFilePath))
                                {
                                    File.Delete(textureFilePath);
                                    texturesRemoved += 1;
                                    logger.Log($"- Removed texture '{textureFilePath}'.");
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Log($"- WARNING: Failed to remove '{textureFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
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

            var currentSubDirectoryNames = new HashSet<string>();
            if (includeSubDirectories)
            {
                foreach (var subDirectoryPath in Directory.EnumerateDirectories(inputDirectory))
                {
                    if (ExternalConversion.IsConversionOutputDirectory(subDirectoryPath))
                        continue;

                    var subDirectoryName = Path.GetFileName(subDirectoryPath);
                    (var added, var updated, var removed) = MakeTexturesFromImagesDirectory(
                        subDirectoryPath,
                        Path.Combine(outputDirectory, subDirectoryName),
                        doFullRebuild,
                        includeSubDirectories,
                        enableSubDirectoryRemoving,
                        logger);

                    currentSubDirectoryNames.Add(subDirectoryName);
                    texturesAdded += added;
                    texturesUpdated += updated;
                    texturesRemoved += removed;
                }

                if (enableSubDirectoryRemoving && textureMakingHistory is not null)
                {
                    // Remove output textures for sub-directories that have been removed:
                    foreach (var subDirectoryName in textureMakingHistory.SubDirectoryNames)
                    {
                        // Remove all textures from the associated output directory, and the directory itself as well if it's empty:
                        if (!currentSubDirectoryNames.Contains(subDirectoryName))
                            texturesRemoved += RemoveOutputTextures(Path.Combine(outputDirectory, subDirectoryName), logger);
                    }
                }
            }

            // Save information about this build operation, to enable future incremental updates:
            var newHistory = new MdlTextureMakingHistory(successfulTextureInputs, currentSubDirectoryNames);
            newHistory.Save(inputDirectory);

            return (texturesAdded, texturesUpdated, texturesRemoved);
        }

        private static bool HasBeenModified(string textureName, string outputTexturePath, MdlTextureSourceFileInfo[] textureSourceFiles, MdlTextureMakingHistory? textureMakingHistory)
        {
            if (textureMakingHistory is null || !textureMakingHistory.Textures.TryGetValue(GetTextureFileName(textureName, textureSourceFiles), out var textureHistory))
                return true;

            if (textureSourceFiles.Length != textureHistory.InputFiles.Length)
                return true;

            if (!textureHistory.OutputFile.HasMatchingFileHash(outputTexturePath))
                return true;

            foreach (var sourceFile in textureSourceFiles)
            {
                var sourceFileName = Path.GetFileName(sourceFile.Path);
                var previousSourceFile = textureHistory.InputFiles.FirstOrDefault(file => Path.GetFileName(file.Path) == sourceFileName);
                if (previousSourceFile is null)
                    return true;

                if (sourceFile.FileSize != previousSourceFile.FileSize || sourceFile.FileHash != previousSourceFile.FileHash || sourceFile.Settings != previousSourceFile.Settings)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Creates and saves a texture file from the given input images and settings.
        /// If <param name="forceRebuild"/> is false, and <paramref name="previousFileHashes"/> and <paramref name="currentFileHashes"/> are provided,
        /// then this method will skip making a texture if it already exists and is up-to-date. It will then also update <paramref name="currentFileHashes"/>
        /// with the file hashes of the given input images.
        /// </summary>
        internal static MdlTexture? MakeTexture(
            string textureName,
            MdlTextureSourceFileInfo[] sourceFiles,
            MdlTextureMakingSettings textureMakingSettings,
            string conversionOutputDirectory,
            Logger logger)
        {
            try
            {
                if (sourceFiles.Any(sourceFile => sourceFile.Settings.Converter is not null && sourceFile.Settings.ConverterArguments is null))
                {
                    logger.Log($"WARNING: some input files for '{textureName}' are missing converter arguments. Skipping texture.");
                    return null;
                }

                // First gather all input files, converting any if necessary:
                var convertedSourceFiles = new List<MdlTextureSourceFileInfo>();
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

                        var conversionOutputPath = Path.Combine(conversionOutputDirectory, textureName);
                        Util.CreateDirectory(conversionOutputDirectory);

                        var outputFilePaths = ExternalConversion.ExecuteConversionCommand(sourceFile.Settings.Converter, sourceFile.Settings.ConverterArguments, sourceFile.Path, conversionOutputPath, logger);
                        if (outputFilePaths.Length < 1)
                            throw new IOException("Unable to find converter output file. An output file must have the same name as the input file (different extensions are ok).");

                        var supportedOutputFilePaths = outputFilePaths.Where(ImageFileIO.CanLoad).ToArray();
                        if (supportedOutputFilePaths.Length < 1)
                            throw new IOException("The converter did not produce a supported file type.");

                        foreach (var supportedOutputFilePath in supportedOutputFilePaths)
                        {
                            var settings = new MdlTextureSettings(sourceFile.Settings);
                            settings.OverrideWith(MdlTextureMakingSettings.GetTextureSettingsFromFilename(supportedOutputFilePath));
                            convertedSourceFiles.Add(new MdlTextureSourceFileInfo(supportedOutputFilePath, 0, new FileHash(), DateTimeOffset.UtcNow, settings));
                        }
                    }
                }

                // Make sure we don't have duplicate input files:
                var isValid = VerifyTextureSourceFiles(textureName, convertedSourceFiles.ToArray(), logger);
                if (!isValid)
                    return null;


                var mainSourceFile = sourceFiles.FirstOrDefault(file => (file.Settings.ColorMask ?? ColorMask.Main) == ColorMask.Main);
                if (mainSourceFile?.Settings.PreservePalette == true)
                    return CreateTextureFromIndexedSourceFile(textureName, mainSourceFile, logger);


                var isModelPortrait = mainSourceFile?.Settings.IsModelPortrait == true;
                var useDmColorRemapping = IsDmBaseInputFilename(textureName) || isModelPortrait;
                var useNewColorRemapping = IsRemapXInputFilename(textureName);

                if (useDmColorRemapping || useNewColorRemapping)
                {
                    var color1SourceFile = sourceFiles.FirstOrDefault(file => file.Settings.ColorMask == ColorMask.Color1);
                    var color2SourceFile = sourceFiles.FirstOrDefault(file => file.Settings.ColorMask == ColorMask.Color2);
                    return CreateColorRemapTexture(textureName, mainSourceFile, color1SourceFile, color2SourceFile, useNewColorRemapping, isModelPortrait, logger);
                }
                else
                {
                    if (mainSourceFile == null)
                    {
                        logger.Log($"- WARNING: missing main input file for '{textureName}', and texture does not support color remapping ({string.Join(", ", sourceFiles.Select(file => file.Path))}). Skipping files.");
                        return null;
                    }

                    return CreateStandardTexture(textureName, mainSourceFile, logger);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"ERROR: Failed to build '{textureName}': {ex.GetType().Name}: '{ex.Message}'.");
                return null;
            }
        }

        private static MdlTexture CreateTextureFromIndexedSourceFile(string textureName, MdlTextureSourceFileInfo sourceFile, Logger logger)
        {
            var indexedImage = ImageFileIO.LoadIndexedImage(sourceFile.Path);
            return new MdlTexture(
                textureName,
                MdlTextureFlags.None,
                indexedImage.Width,
                indexedImage.Height,
                indexedImage.ImageData,
                indexedImage.Palette);
        }

        private static MdlTexture CreateStandardTexture(string textureName, MdlTextureSourceFileInfo sourceFile, Logger logger)
        {
            using (var image = ImageFileIO.LoadImage(sourceFile.Path))
            {
                var transparencyThreshold = Math.Clamp(sourceFile.Settings.TransparencyThreshold ?? Constants.DefaultTransparencyThreshold, 0, 255);
                var hasTransparency = ContainsTransparentPixels(image, transparencyThreshold);
                if (!hasTransparency && sourceFile.Settings.TransparencyColor != null)
                    hasTransparency = ContainsTransparencyColor(image, sourceFile.Settings.TransparencyColor.Value);
                var transparencyPredicate = hasTransparency ? Util.MakeTransparencyPredicate(transparencyThreshold, sourceFile.Settings.TransparencyColor) : null;

                var paletteSize = Constants.MaxPaletteSize;
                if (hasTransparency)
                    paletteSize -= 1;

                var indexedImage = ColorQuantization.QuantizeImage(
                    image,
                    paletteSize,
                    sourceFile.Settings.DitheringAlgorithm ?? DitheringAlgorithm.FloydSteinberg,
                    sourceFile.Settings.DitherScale ?? 0.75f,
                    transparencyPredicate);

                var palette = indexedImage.Palette.Concat(Enumerable.Range(0, Constants.MaxPaletteSize - indexedImage.Palette.Length).Select(i => new Rgba32())).ToArray();
                if (hasTransparency)
                    palette[Constants.TransparentColorIndex] = new Rgba32(0, 0, 255); // NOTE: Deep blue is a convention, not a necessity.

                return new MdlTexture(
                    textureName,
                    hasTransparency ? MdlTextureFlags.MaskedTransparency : MdlTextureFlags.None,
                    indexedImage.Width,
                    indexedImage.Height,
                    indexedImage.ImageData,
                    palette);
            }
        }

        private static MdlTexture? CreateColorRemapTexture(
            string textureName,
            MdlTextureSourceFileInfo? mainSourceFile,
            MdlTextureSourceFileInfo? color1SourceFile,
            MdlTextureSourceFileInfo? color2SourceFile,
            bool useNewColorRemapping,
            bool swapColor12,
            Logger logger)
        {
            // Determine remap color counts:
            var color1Count = 32;
            var color2Count = 32;
            var mainColorCount = 192;
            var color1PaletteStart = 160;

            if (useNewColorRemapping)
            {
                GetRemapColorCounts(mainSourceFile, color1SourceFile, color2SourceFile, out color1PaletteStart, out color1Count, out color2Count);
                mainColorCount = Constants.MaxPaletteSize - color1Count - color2Count;
            }

            if (mainColorCount < 0)
            {
                logger.Log($"- WARNING: total color count for remap colors in '{textureName}' is larger than {Constants.MaxPaletteSize} ({color1Count + color2Count}). Skipping file(s).");
                return null;
            }


            using (var colorRemapInputImages = new DisposableList<ColorRemapInputImage>(LoadColorRemapInputImages(
                (mainSourceFile, mainColorCount),
                (color1SourceFile, color1Count),
                (color2SourceFile, color2Count))))
            {
                var width = colorRemapInputImages[0].Image.Width;
                var height = colorRemapInputImages[0].Image.Height;
                var imageData = new byte[width * height];
                var palette = new Rgba32[Constants.MaxPaletteSize];

                var coverageMap = GetCoverageMap(colorRemapInputImages);

                var firstInputImageType = colorRemapInputImages.First().Type;
                var paletteOffset = firstInputImageType == ColorMask.Main ? 0 :
                                  firstInputImageType == ColorMask.Color1 ? color1PaletteStart :
                                                                            color1PaletteStart + color1Count;

                // Prepare the image data, and generate the palette by combining the palettes from the standard and remap color areas:
                foreach (var colorRemapInputImage in colorRemapInputImages)
                {
                    var hasNonContiguousPaletteRange = colorRemapInputImage.Type == ColorMask.Main && colorRemapInputImage.ColorCount > color1PaletteStart;

                    var coverageID = (byte)colorRemapInputImage.Type;
                    var indexedImage = ColorQuantization.QuantizeImage(
                        colorRemapInputImage.Image,
                        colorRemapInputImage.ColorCount,
                        colorRemapInputImage.DitheringAlgorithm,
                        colorRemapInputImage.DitherScale,
                        colorRemapInputImage.IsTransparent,
                        (x, y, color) => coverageMap[y * width + x] != coverageID);

                    // Copy the image data and palette for this color map into the final texture:
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (coverageMap[y * width + x] == coverageID)
                                imageData[y * width + x] = (byte)(paletteOffset + indexedImage[x, y]);
                        }
                    }
                    Array.Copy(indexedImage.Palette, 0, palette, paletteOffset, indexedImage.Palette.Length);

                    if (hasNonContiguousPaletteRange)
                    {
                        // The main image colors may be interrupted by the color1 and color2 ranges:
                        if (indexedImage.Palette.Length > color1PaletteStart)
                        {
                            var offset = (byte)(color1Count + color2Count);
                            for (int i = 0; i < imageData.Length; i++)
                            {
                                if (coverageMap[i] == coverageID && imageData[i] >= color1PaletteStart)
                                    imageData[i] += offset;
                            }
                            Array.Copy(indexedImage.Palette, color1PaletteStart, palette, color1PaletteStart + offset, indexedImage.Palette.Length - color1PaletteStart);
                        }

                        paletteOffset += color1PaletteStart;
                    }
                    else
                    {
                        paletteOffset += colorRemapInputImage.ColorCount;
                    }
                }

                // Workaround for a bug/inconsistency in the engine:
                if (swapColor12 && color1Count > 0 && color2Count > 0)
                {
                    // Swap color1 and color2. We can't just swap the input images, because that would change the end result if there is overlap between them, so we'll do it afterwards:
                    var color1PaletteEnd = color1PaletteStart + color1Count - 1;
                    var color2PaletteEnd = color1PaletteStart + color1Count + color2Count - 1;
                    for (int i = 0; i < imageData.Length; i++)
                    {
                        var index = imageData[i];
                        if (index >= color1PaletteStart && index <= color2PaletteEnd)
                        {
                            if (index > color1PaletteEnd)
                                imageData[i] -= (byte)color1Count;
                            else
                                imageData[i] += (byte)color1Count;
                        }
                    }

                    var color1Palette = palette.Skip(color1PaletteStart).Take(color1Count).ToArray();
                    var color2Palette = palette.Skip(color1PaletteStart + color1Count).Take(color2Count).ToArray();

                    Array.Copy(color2Palette, 0, palette, color1PaletteStart, color2Count);
                    Array.Copy(color1Palette, 0, palette, color1PaletteStart + color2Count, color1Count);
                }

                return new MdlTexture(textureName, MdlTextureFlags.None, width, height, imageData, palette);
            }
        }


        private static string GetTextureFileName(string textureName, IEnumerable<MdlTextureSourceFileInfo> sourceFiles)
        {
            if (IsRemapXInputFilename(textureName))
            {
                GetRemapColorCounts(sourceFiles, out var color1Start, out var color1Count, out var color2Count);

                var color1End = color1Start + color1Count - 1;
                var color2End = color1End + color2Count;
                return $"{textureName}_{color1Start:D3}_{color1End:D3}_{color2End:D3}";
            }
            else
            {
                return textureName;
            }
        }

        private static void GetRemapColorCounts(IEnumerable<MdlTextureSourceFileInfo> sourceFiles, out int color1Start, out int color1Count, out int color2Count)
        {
            var mainSourceFile = sourceFiles.FirstOrDefault(sourceFile => (sourceFile.Settings.ColorMask ?? ColorMask.Main) == ColorMask.Main);
            var color1SourceFile = sourceFiles.FirstOrDefault(sourceFile => sourceFile.Settings.ColorMask == ColorMask.Color1);
            var color2SourceFile = sourceFiles.FirstOrDefault(sourceFile => sourceFile.Settings.ColorMask == ColorMask.Color2);

            GetRemapColorCounts(mainSourceFile, color1SourceFile, color2SourceFile, out color1Start, out color1Count, out color2Count);
        }

        private static void GetRemapColorCounts(
            MdlTextureSourceFileInfo? mainSourceFile,
            MdlTextureSourceFileInfo? color1SourceFile,
            MdlTextureSourceFileInfo? color2SourceFile,
            out int color1Start,
            out int color1Count,
            out int color2Count)
        {
            color1Count = color1SourceFile == null ? 0 : color1SourceFile.Settings.ColorCount ?? 32;
            color2Count = color2SourceFile == null ? 0 : color2SourceFile.Settings.ColorCount ?? 32;
            color1Start = mainSourceFile?.Settings.ColorCount == null ? Constants.MaxPaletteSize - color1Count - color2Count : mainSourceFile.Settings.ColorCount.Value;
        }

        private static bool IsDmBaseInputFilename(string textureName) => Regex.IsMatch(textureName, "^dm_base$", RegexOptions.IgnoreCase);

        private static bool IsRemapXInputFilename(string textureName) => Regex.IsMatch(textureName, "^remap[0-9a-z]$", RegexOptions.IgnoreCase);

        class ColorRemapInputImage : IDisposable
        {
            public Image<Rgba32> Image { get; }
            public ColorMask Type { get; }
            public int ColorCount { get; }
            public Func<Rgba32, bool> IsTransparent { get; }
            public DitheringAlgorithm DitheringAlgorithm { get; }
            public float DitherScale { get; }

            public ColorRemapInputImage(Image<Rgba32> image, ColorMask type, int colorCount, Func<Rgba32, bool> isTransparent, DitheringAlgorithm ditheringAlgorithm, float ditherScale)
            {
                Image = image;
                Type = type;
                ColorCount = colorCount;
                IsTransparent = isTransparent;
                DitheringAlgorithm = ditheringAlgorithm;
                DitherScale = ditherScale;
            }

            public void Dispose() => Image.Dispose();
        }

        private static ColorRemapInputImage[] LoadColorRemapInputImages(params (MdlTextureSourceFileInfo?, int)[] inputs)
        {
            var inputImages = new List<ColorRemapInputImage>();
            foreach ((var sourceFile, int colorCount) in inputs)
            {
                try
                {
                    if (sourceFile is null)
                        continue;

                    var image = ImageFileIO.LoadImage(sourceFile.Path);
                    var isTransparentPredicate = Util.MakeTransparencyPredicate(Math.Clamp(sourceFile.Settings.TransparencyThreshold ?? Constants.DefaultTransparencyThreshold, 0, 255), sourceFile.Settings.TransparencyColor);
                    inputImages.Add(new ColorRemapInputImage(
                        image,
                        sourceFile.Settings.ColorMask ?? ColorMask.Main,
                        colorCount,
                        isTransparentPredicate,
                        sourceFile.Settings.DitheringAlgorithm ?? DitheringAlgorithm.FloydSteinberg,
                        sourceFile.Settings.DitherScale ?? 0.75f));
                }
                catch (Exception ex)
                {
                    foreach (var inputImage in inputImages)
                        inputImage.Dispose();

                    throw;
                }
            }
            return inputImages.ToArray();
        }

        private static byte[] GetCoverageMap(IReadOnlyList<ColorRemapInputImage> colorRemapInputImages)
        {
            var width = colorRemapInputImages[0].Image.Width;
            var height = colorRemapInputImages[0].Image.Height;

            var coverageMap = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Check images in reverse order, so color2 takes priority over color1, and color1 over main:
                    for (int i = colorRemapInputImages.Count - 1; i >= 0; i--)
                    {
                        var colorRemapInputImage = colorRemapInputImages[i];
                        if (colorRemapInputImage.Type == ColorMask.Main)
                            break;

                        var color = colorRemapInputImage.Image[x, y];
                        if (!colorRemapInputImage.IsTransparent(color))
                        {
                            coverageMap[y * width + x] = (byte)colorRemapInputImage.Type;
                            break;
                        }
                    }
                }
            }
            return coverageMap;
        }


        private static bool ContainsTransparentPixels(Image<Rgba32> image, int transparencyThreshold)
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image[x, y].A < transparencyThreshold)
                        return true;
                }
            }
            return false;
        }

        private static bool ContainsTransparencyColor(Image<Rgba32> image, Rgba32 transparencyColor)
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image[x, y] == transparencyColor)
                        return true;
                }
            }
            return false;
        }

        private static bool VerifyTextureSourceFiles(string textureName, MdlTextureSourceFileInfo[] textureSourceFiles, Logger logger)
        {
            // Do we have conflicting input files (e.g. "foo.png" and "foo.jpg", or multiple images for the same color mask)?
            if (textureSourceFiles.Length > 1)
            {
                var hasDuplicateColorMasks = textureSourceFiles
                    .GroupBy(file => file.Settings.ColorMask ?? ColorMask.Main)
                    .Any(colorMaskGroup => colorMaskGroup.Count() > 1);
                if (hasDuplicateColorMasks)
                {
                    logger.Log($"- WARNING: conflicting input files detected for '{textureName}' ({string.Join(", ", textureSourceFiles.Select(file => file.Path))}). Skipping files.");
                    return false;
                }
            }

            if (!IsDmBaseInputFilename(textureName) && !IsRemapXInputFilename(textureName) && !textureSourceFiles.Any(file => file.Settings.IsModelPortrait == true))
            {
                if (textureSourceFiles.Any(file => (file.Settings.ColorMask ?? ColorMask.Main) != ColorMask.Main))
                {
                    logger.Log($"- WARNING: color1 and color2 overlays detected for '{textureName}', which does not support color remapping ({string.Join(", ", textureSourceFiles.Select(file => file.Path))}). Skipping file(s).");
                    return false;
                }
            }

            // Everything is looking good so far:
            return true;
        }


        private static int RemoveOutputTextures(string directory, Logger logger)
        {
            if (!Directory.Exists(directory))
                return 0;

            var texturesRemoved = 0;

            // First remove all texture files:
            foreach (var textureFilePath in Directory.EnumerateFiles(directory, "*.bmp"))
            {
                try
                {
                    File.Delete(textureFilePath);
                    texturesRemoved += 1;
                }
                catch (Exception ex)
                {
                    logger.Log($"Failed to remove '{textureFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            // Then recursively try removing sub-directories:
            foreach (var subDirectoryPath in Directory.EnumerateDirectories(directory))
                texturesRemoved += RemoveOutputTextures(subDirectoryPath, logger);

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

            return texturesRemoved;
        }


        /// <summary>
        /// Returns all potential input files for the given directory.
        /// Bookkeeping files and directories are ignored.
        /// </summary>
        internal static IEnumerable<string> GetInputFilePaths(string inputDirectory)
        {
            foreach (var path in Directory.EnumerateFiles(inputDirectory))
            {
                if (MdlTextureMakingSettings.IsConfigurationFile(path) || MdlTextureMakingHistory.IsHistoryFile(path))
                    continue;

                yield return path;
            }
        }
    }
}
