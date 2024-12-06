using Shared.FileFormats;
using Shared;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.RegularExpressions;

namespace WadMaker
{
    public static class WadMaking
    {
        static Regex AnimatedTextureNameRegex = new Regex(@"^\+[0-9A-J]");


        public static void MakeWad(string inputDirectory, string outputWadFilePath, bool fullRebuild, bool includeSubDirectories, Logger logger)
        {
            if (!Directory.Exists(inputDirectory))
            {
                if (File.Exists(inputDirectory))
                    throw new InvalidUsageException("Unable to create or update wad file: the input must be a directory, not a file.");
                else
                    throw new InvalidUsageException($"Unable to create or update wad file: the input directory '{inputDirectory}' does not exist.");
            }

            if (Path.GetExtension(outputWadFilePath).ToLowerInvariant() != ".wad")
                throw new InvalidUsageException($"Unable to create or update wad file: the output must be a .wad file.");

            var stopwatch = Stopwatch.StartNew();

            var texturesAdded = 0;
            var texturesUpdated = 0;
            var texturesRemoved = 0;
            var errorCount = 0;

            var wadMakingSettings = WadMakingSettings.Load(inputDirectory);
            var updateExistingWad = !fullRebuild && File.Exists(outputWadFilePath);
            var wad = updateExistingWad ? LoadWad(outputWadFilePath, logger) : new Wad();
            var lastWadUpdateTime = updateExistingWad ? new FileInfo(outputWadFilePath).LastWriteTimeUtc : (DateTime?)null;
            var wadTextureNames = wad.Textures.Select(texture => texture.Name.ToLowerInvariant()).ToHashSet();
            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);
            var isDecalsWad = Path.GetFileNameWithoutExtension(outputWadFilePath).ToLowerInvariant() == "decals";

            logger.Log($"{(updateExistingWad ? "Updating existing" : "Creating new")} {(isDecalsWad ? "decals wad file" : "wad file")}.");

            // Multiple files can map to the same texture, due to different extensions and upper/lower-case differences.
            // We'll group files by texture name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory, "*", includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(path => !ExternalConversion.IsConversionOutputDirectory(path))
                .ToHashSet();
            var textureImagePaths = allInputDirectoryFiles
                .Where(path => !WadMakingSettings.IsConfigurationFile(path))
                .Where(path =>
                {
                    var settings = wadMakingSettings.GetTextureSettings(Path.GetFileName(path)).settings;
                    return settings.Ignore != true && (ImageReading.IsSupported(path) || settings.Converter != null);
                })
                .Where(path => !path.Contains(".mipmap"))
                .GroupBy(path => WadMakingSettings.GetTextureName(path));

            // Check for new and updated images:
            try
            {
                foreach (var imagePathsGroup in textureImagePaths)
                {
                    var textureName = imagePathsGroup.Key;
                    if (!IsValidTextureName(textureName))
                    {
                        logger.Log($"- WARNING: '{textureName}' is not a valid texture name ({string.Join(", ", imagePathsGroup)}). Skipping file(s).");
                        continue;
                    }
                    else if (textureName.Length > 15)
                    {
                        logger.Log($"- WARNING: The name '{textureName}' is too long ({string.Join(", ", imagePathsGroup)}). Skipping file(s).");
                        continue;
                    }
                    else if (imagePathsGroup.Count() > 1)
                    {
                        logger.Log($"- WARNING: multiple input files detected for '{textureName}' ({string.Join(", ", imagePathsGroup)}). Skipping files.");
                        continue;
                    }
                    // NOTE: Texture dimensions (which must be multiples of 16) are checked later, in CreateTextureFromImage.


                    var filePath = imagePathsGroup.Single();
                    (var textureSettings, var lastSettingsChangeTime) = wadMakingSettings.GetTextureSettings(Path.GetFileName(filePath));

                    var isExistingImage = wadTextureNames.Contains(textureName.ToLowerInvariant());
                    if (isExistingImage && updateExistingWad)
                    {
                        // NOTE: A texture will not be rebuilt if one of its mipmap files has been removed. In order to detect such cases,
                        //       WadMaker would need to store additional bookkeeping data, but right now that doesn't seem worth the trouble.
                        // NOTE: Mipmaps must have the same extension as the main image file.
                        var isImageUpdated = GetMipmapFilePaths(filePath)
                            .Prepend(filePath)
                            .Where(allInputDirectoryFiles.Contains)
                            .Select(path => new FileInfo(path).LastWriteTimeUtc)
                            .Any(dateTime => dateTime > lastWadUpdateTime);
                        if (!isImageUpdated && lastSettingsChangeTime < lastWadUpdateTime)
                        {
                            //Log($"No modifications detected for '{textureName}' ({filePath}). Skipping file.");
                            continue;
                        }
                    }

                    try
                    {
                        var imageFilePath = filePath;
                        if (textureSettings.Converter != null)
                        {
                            if (textureSettings.ConverterArguments == null)
                                throw new InvalidDataException($"Unable to convert '{filePath}': missing converter arguments.");

                            imageFilePath = Path.Combine(conversionOutputDirectory, textureName);
                            CreateDirectory(conversionOutputDirectory);

                            var outputFilePaths = ExternalConversion.ExecuteConversionCommand(textureSettings.Converter, textureSettings.ConverterArguments, filePath, imageFilePath, logger);
                            if (imageFilePath.Length < 1)
                                throw new IOException("Unable to find converter output file. An output file must have the same name as the input file (different extensions are ok).");

                            var supportedOutputFilePaths = outputFilePaths.Where(ImageReading.IsSupported).ToArray();
                            if (supportedOutputFilePaths.Length < 1)
                                throw new IOException("The converter did not produce a supported file type.");
                            else if (supportedOutputFilePaths.Length > 1)
                                throw new IOException("The converted produced multiple supported file types. Only one output file should be created.");

                            imageFilePath = supportedOutputFilePaths[0];
                        }

                        // Create texture from image:
                        var texture = CreateTextureFromImage(imageFilePath, textureName, textureSettings, isDecalsWad);

                        if (isExistingImage)
                        {
                            // Update (replace) existing texture:
                            for (int i = 0; i < wad.Textures.Count; i++)
                            {
                                if (wad.Textures[i].Name == texture.Name)
                                {
                                    wad.Textures[i] = texture;
                                    break;
                                }
                            }
                            texturesUpdated += 1;
                            logger.Log($"- Updated texture '{textureName}' (from '{filePath}').");
                        }
                        else
                        {
                            // Add new texture:
                            wad.Textures.Add(texture);
                            wadTextureNames.Add(textureName);
                            texturesAdded += 1;
                            logger.Log($"- Added texture '{textureName}' (from '{filePath}').");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"- ERROR: failed to build '{textureName}': {ex.GetType().Name}: '{ex.Message}'.");
                        errorCount += 1;
                    }
                }

                if (updateExistingWad)
                {
                    // Check for removed images:
                    var directoryTextureNames = textureImagePaths
                        .Select(group => group.Key)
                        .ToHashSet();
                    foreach (var textureName in wadTextureNames)
                    {
                        if (!directoryTextureNames.Contains(textureName))
                        {
                            // Delete texture:
                            wad.Textures.Remove(wad.Textures.First(texture => texture.Name.ToLowerInvariant() == textureName));
                            texturesRemoved += 1;
                            logger.Log($"- Removed texture '{textureName}'.");
                        }
                    }
                }

                // Finally, save the wad file:
                CreateDirectory(Path.GetDirectoryName(outputWadFilePath));
                wad.Save(outputWadFilePath);
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

            if (updateExistingWad)
                logger.Log($"Updated '{outputWadFilePath}' from '{inputDirectory}': added {texturesAdded}, updated {texturesUpdated} and removed {texturesRemoved} textures, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
            else
                logger.Log($"Created '{outputWadFilePath}', with {texturesAdded} textures from '{inputDirectory}', in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");

            if (errorCount == 0)
                logger.Log("No errors.");
            else
                logger.Log($"{errorCount} errors.");
        }


        // TODO: Really allow all characters in this range? Aren't there some characters that may cause trouble (in .map files, for example, such as commas, parenthesis, etc.?)
        static bool IsValidTextureName(string name) => name.All(c => c > 0 && c < 256 && c != ' ');

        static IEnumerable<string> GetMipmapFilePaths(string path)
        {
            for (int mipmap = 1; mipmap <= 3; mipmap++)
                yield return Path.ChangeExtension(path, $".mipmap{mipmap}{Path.GetExtension(path)}");
        }

        static Texture CreateTextureFromImage(string path, string textureName, TextureSettings textureSettings, bool isDecalsWad)
        {
            // Load the main texture image, and any available mipmap images:
            using (var images = new DisposableList<Image<Rgba32>?>(GetMipmapFilePaths(path).Prepend(path)
                .Select(imagePath => File.Exists(imagePath) ? ImageReading.ReadImage(imagePath) : null)))
            {
                // Verify image sizes:
                var mainImage = images[0]!;
                if (mainImage.Width % 16 != 0 || mainImage.Height % 16 != 0)
                    throw new InvalidDataException($"Texture '{path}' width or height is not a multiple of 16.");

                for (int i = 1; i < images.Count; i++)
                {
                    var mipmapImage = images[i];
                    if (mipmapImage != null && (mipmapImage.Width != mainImage.Width >> i || mipmapImage.Height != mainImage.Height >> i))
                        throw new InvalidDataException($"Mipmap {i} for texture '{path}' width or height does not match texture size.");
                }

                if (isDecalsWad)
                    return CreateDecalTexture(textureName, images.ToArray(), textureSettings);


                var filename = Path.GetFileName(path);
                var isTransparentTexture = filename.StartsWith("{");
                var isAnimatedTexture = AnimatedTextureNameRegex.IsMatch(filename);
                var isWaterTexture = filename.StartsWith("!");

                // Create a suitable palette, taking special texture types into account:
                var transparencyThreshold = isTransparentTexture ? Math.Clamp(textureSettings.TransparencyThreshold ?? 128, 0, 255) : 0;
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

                var colorHistogram = ColorQuantization.GetColorHistogram(images.Where(image => image != null)!, isTransparentPredicate);
                var maxColors = 256 - (isTransparentTexture ? 1 : 0) - (isWaterTexture ? 2 : 0);
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

                // Make palette adjustments for special textures:
                if (isWaterTexture)
                {
                    var fogColor = textureSettings.WaterFogColor ?? ColorQuantization.GetAverageColor(colorHistogram);
                    var fogIntensity = new Rgba32((byte)Math.Clamp(textureSettings.WaterFogColor?.A ?? (int)((1f - GetBrightness(fogColor)) * 255), 0, 255), 0, 0);

                    colorClusters = colorClusters.Take(3)
                        .Append((fogColor, new[] { fogColor }))         // Slot 3: water fog color
                        .Append((fogIntensity, new[] { fogIntensity })) // Slot 4: fog intensity (stored in red channel)
                        .Concat(colorClusters.Skip(3))
                        .ToArray();
                }

                if (isTransparentTexture)
                {
                    var colorKey = new Rgba32(0, 0, 255);
                    colorClusters = colorClusters
                        .Append((colorKey, new[] { colorKey }))         // Slot 255: used for transparent pixels
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

                // Create any missing mipmaps:
                for (int i = 1; i < images.Count; i++)
                {
                    if (images[i] == null)
                        images[i] = mainImage.Clone(context => context.Resize(mainImage.Width >> i, mainImage.Height >> i));
                }

                // Create texture data:
                var textureData = images
                    .Select(image => CreateTextureData(image!, palette, colorIndexMappingCache, textureSettings, isTransparentPredicate, disableDithering: isAnimatedTexture))
                    .ToArray();

                return Texture.CreateMipmapTexture(
                    name: textureName,
                    width: mainImage.Width,
                    height: mainImage.Height,
                    imageData: textureData[0],
                    palette: palette,
                    mipmap1Data: textureData[1],
                    mipmap2Data: textureData[2],
                    mipmap3Data: textureData[3]);
            }
        }

        static Texture CreateDecalTexture(string name, Image<Rgba32>?[] images, TextureSettings textureSettings)
        {
            // Create any missing mipmaps (this does not affect the palette, so it can be done up-front):
            var mainImage = images[0]!;
            for (int i = 1; i < images.Length; i++)
            {
                if (images[i] == null)
                    images[i] = mainImage.Clone(context => context.Resize(mainImage.Width >> i, mainImage.Height >> i));
            }

            // The last palette color determines the color of the decal. All other colors are irrelevant - palette indexes are treated as alpha values instead.
            var decalColor = textureSettings.DecalColor ?? ColorQuantization.GetAverageColor(ColorQuantization.GetColorHistogram(images!, color => color.A == 0));
            var palette = Enumerable.Range(0, 255)
                .Select(i => new Rgba32((byte)i, (byte)i, (byte)i))
                .Append(decalColor)
                .ToArray();

            var textureData = images
                .Select(CreateDecalTextureData!)
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: name,
                width: mainImage.Width,
                height: mainImage.Height,
                imageData: textureData[0],
                palette: palette,
                mipmap1Data: textureData[1],
                mipmap2Data: textureData[2],
                mipmap3Data: textureData[3]);


            byte[] CreateDecalTextureData(Image<Rgba32> image)
            {
                var mode = textureSettings.DecalTransparencySource ?? DecalTransparencySource.AlphaChannel;
                var getPaletteIndex = (mode == DecalTransparencySource.AlphaChannel) ? (Func<Rgba32, byte>)(color => color.A) :
                                                                          (Func<Rgba32, byte>)(color => (byte)((color.R + color.G + color.B) / 3));

                var data = new byte[image.Width * image.Height];
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < image.Height; y++)
                    {
                        var rowSpan = accessor.GetRowSpan(y);
                        for (int x = 0; x < image.Width; x++)
                        {
                            var color = rowSpan[x];
                            data[y * image.Width + x] = getPaletteIndex(color);
                        }
                    }
                });
                return data;
            }
        }


        static int GetBrightness(Rgba32 color) => (int)(color.R * 0.21 + color.G * 0.72 + color.B * 0.07);

        // TODO: Disable dithering for animated textures again? It doesn't actually remove all flickering, because different frames can still have different palettes...!
        static byte[] CreateTextureData(
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
                    return ApplyPaletteWithoutDithering();

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(image, palette, getColorIndex, textureSettings.DitherScale ?? 0.75f, isTransparent);
            }


            byte[] ApplyPaletteWithoutDithering()
            {
                var textureData = new byte[image.Width * image.Height];
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < image.Height; y++)
                    {
                        var rowSpan = accessor.GetRowSpan(y);
                        for (int x = 0; x < image.Width; x++)
                        {
                            var color = rowSpan[x];
                            textureData[y * image.Width + x] = (byte)getColorIndex(color);
                        }
                    }
                });
                return textureData;
            }
        }


        static Wad LoadWad(string filePath, Logger logger)
        {
            logger.Log($"Loading wad file: '{filePath}'.");
            return Wad.Load(filePath, (index, name, exception) => logger.Log($"- Failed to load texture #{index} ('{name}'): {exception.GetType().Name}: '{exception.Message}'."));
        }

        static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
