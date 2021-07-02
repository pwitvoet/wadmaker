using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Shared.FileFormats;
using Shared;

namespace WadMaker
{
    class ProgramSettings
    {
        // Build settings:
        public bool FullRebuild { get; set; }               // -full        forces a full rebuild instead of an incremental one
        public bool IncludeSubDirectories { get; set; }     // -subdirs     also include images in sub-directories

        // Extract settings:
        public bool Extract { get; set; }                   //              Texture extraction mode is enabled when the first argument (path) is a wad or bsp file.
        public bool ExtractMipmaps { get; set; }            // -mipmaps     also extract mipmaps
        public bool OverwriteExistingFiles { get; set; }    // -overwrite   extract mode only, enables overwriting of existing image files (off by default)

        // Bsp settings:
        public bool RemoveEmbeddedTextures { get; set; }    // -remove      removes embedded textures from the given bsp file

        // Other settings:
        public string InputDirectory { get; set; }          // Build mode only
        public string InputFilePath { get; set; }           // Wad or bsp path (output in build mode, input in extract mode).
        public string OutputDirectory { get; set; }         // Extract mode only
        public string OutputFilePath { get; set; }          // Output bsp path (when removing embedded textures)

        public bool DisableFileLogging { get; set; }        // -nologfile   disables logging to a file (parent-directory\wadmaker.log)
    }

    class Program
    {
        static Regex AnimatedTextureNameRegex = new Regex(@"^\+[0-9A-J]");
        static TextWriter LogFile;


        static void Main(string[] args)
        {
            try
            {
                Log($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");

                var settings = ParseArguments(args);
                if (settings.Extract)
                {
                    ExtractTextures(settings.InputFilePath, settings.OutputDirectory, settings.ExtractMipmaps, settings.OverwriteExistingFiles);
                }
                else if (settings.RemoveEmbeddedTextures)
                {
                    RemoveEmbeddedTextures(settings.InputFilePath, settings.OutputFilePath);
                }
                else
                {
                    if (!settings.DisableFileLogging)
                    {
                        var logFilePath = Path.Combine(Path.GetDirectoryName(settings.InputDirectory), $"wadmaker - {Path.GetFileName(settings.InputDirectory)}.log");
                        LogFile = new StreamWriter(logFilePath, false, Encoding.UTF8);
                        LogFile.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");
                    }
                    MakeWad(settings.InputDirectory, settings.InputFilePath, settings.FullRebuild, settings.IncludeSubDirectories);
                }
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.GetType().Name}: '{ex.Message}'.");
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
                    case "-full": settings.FullRebuild = true; break;
                    case "-subdirs": settings.IncludeSubDirectories = true; break;
                    case "-mipmaps": settings.ExtractMipmaps = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;
                    case "-remove": settings.RemoveEmbeddedTextures = true; break;
                    case "-nologfile": settings.DisableFileLogging = true; break;
                    default: throw new ArgumentException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new ArgumentException("Missing input folder (for wad building) or file (for texture extraction) argument.");

            if (paths[0].EndsWith(".wad") || (paths[0].EndsWith(".bsp") && !settings.RemoveEmbeddedTextures))
                settings.Extract = true;


            if (settings.Extract)
            {
                // Texture extraction requires a wad or bsp file path, and optionally an output folder:
                settings.InputFilePath = args[index++];

                if (index < args.Length)
                    settings.OutputDirectory = args[index++];
                else
                    settings.OutputDirectory = Path.Combine(Path.GetDirectoryName(settings.InputFilePath), Path.GetFileNameWithoutExtension(settings.InputFilePath) + "_extracted");
            }
            else if (settings.RemoveEmbeddedTextures)
            {
                // Embedded texture removal requires a bsp file path, and optionally an output bsp file path:
                settings.InputFilePath = args[index++];

                if (index < args.Length)
                    settings.OutputFilePath = args[index++];
                else
                    settings.OutputFilePath = settings.InputFilePath;
            }
            else
            {
                // Wad making requires a directory path, and optionally an output wad file path:
                settings.InputDirectory = args[index++];

                if (index < args.Length)
                    settings.InputFilePath = args[index++];
                else
                    settings.InputFilePath = $"{Path.GetFileName(settings.InputDirectory)}.wad";

                if (!Path.IsPathRooted(settings.InputFilePath))
                    settings.InputFilePath = Path.Combine(Path.GetDirectoryName(settings.InputDirectory), settings.InputFilePath);
            }

            return settings;
        }

        // TODO: Also create a wadmaker.config file, if the wad contained fonts or simple images (mipmap textures are the default behavior, so those don't need a config,
        //       unless the user wants to create a wad file and wants different settings for those images such as different dithering, etc.)
        static void ExtractTextures(string inputFilePath, string outputDirectory, bool extractMipmaps, bool overwriteExistingFiles)
        {
            var stopwatch = Stopwatch.StartNew();

            var imageFilesCreated = 0;

            var textures = new List<Texture>();
            if (inputFilePath.EndsWith(".bsp"))
                textures = Bsp.GetEmbeddedTextures(inputFilePath);
            else
                textures = Wad.Load(inputFilePath).Textures;

            Directory.CreateDirectory(outputDirectory);

            var isDecalsWad = Path.GetFileName(inputFilePath).ToLowerInvariant() == "decals.wad";
            foreach (var texture in textures)
            {
                var maxMipmap = extractMipmaps ? 4 : 1;
                for (int mipmap = 0; mipmap < maxMipmap; mipmap++)
                {
                    try
                    {
                        var filePath = Path.Combine(outputDirectory, texture.Name + $"{(mipmap > 0 ? ".mipmap" + mipmap : "")}.png");
                        if (!overwriteExistingFiles && File.Exists(filePath))
                        {
                            Log($"WARNING: {filePath} already exist. Skipping texture.");
                            continue;
                        }

                        using (var image = isDecalsWad ? DecalTextureToImage(texture, mipmap) : TextureToImage(texture, mipmap))
                        {
                            image.SaveAsPng(filePath);
                            imageFilesCreated += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: failed to extract '{texture.Name}'{(mipmap > 0 ? $" (mipmap {mipmap})" : "")}: {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }
            }

            Log($"Extracted {imageFilesCreated} images from {textures.Count} textures from {inputFilePath} to {outputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        static void RemoveEmbeddedTextures(string bspFilePath, string outputFilePath)
        {
            var stopwatch = Stopwatch.StartNew();

            if (!bspFilePath.EndsWith(".bsp"))
                throw new ArgumentException("Removing embedded textures requires a .bsp file.");

            Log($"Removing embedded textures from '{bspFilePath}' and saving the result to '{outputFilePath}'.");

            var removedTextureCount = Bsp.RemoveEmbeddedTextures(bspFilePath, outputFilePath);

            Log($"Removed {removedTextureCount} embedded textures from {bspFilePath} in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        static void MakeWad(string inputDirectory, string outputWadFilePath, bool fullRebuild, bool includeSubDirectories)
        {
            var stopwatch = Stopwatch.StartNew();

            var texturesAdded = 0;
            var texturesUpdated = 0;
            var texturesRemoved = 0;

            var wadMakingSettings = WadMakingSettings.Load(inputDirectory);
            var updateExistingWad = !fullRebuild && File.Exists(outputWadFilePath);
            var wad = updateExistingWad ? Wad.Load(outputWadFilePath) : new Wad();
            var lastWadUpdateTime = updateExistingWad ? new FileInfo(outputWadFilePath).LastWriteTimeUtc : (DateTime?)null;
            var wadTextureNames = wad.Textures.Select(texture => texture.Name.ToLowerInvariant()).ToHashSet();
            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);
            var isDecalsWad = Path.GetFileNameWithoutExtension(outputWadFilePath).ToLowerInvariant() == "decals";

            // Multiple files can map to the same texture, due to different extensions and upper/lower-case differences.
            // We'll group files by texture name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory, "*", includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(path => !ExternalConversion.IsConversionOutputDirectory(path))
                .ToHashSet();
            var textureImagePaths = allInputDirectoryFiles
                .Where(path => ImageReading.IsSupported(path) || wadMakingSettings.GetTextureSettings(Path.GetFileName(path)).settings.Converter != null)
                .Where(path => !path.Contains(".mipmap"))
                .Where(path => !WadMakingSettings.IsConfigurationFile(path))
                .GroupBy(path => GetTextureName(path));

            // Check for new and updated images:
            try
            {
                foreach (var imagePathsGroup in textureImagePaths)
                {
                    var textureName = imagePathsGroup.Key;
                    if (!IsValidTextureName(textureName))
                    {
                        Log($"WARNING: '{textureName}' is not a valid texture name ({string.Join(", ", imagePathsGroup)}). Skipping file(s).");
                        continue;
                    }
                    else if (textureName.Length > 15)
                    {
                        Log($"WARNING: The name '{textureName}' is too long ({string.Join(", ", imagePathsGroup)}). Skipping file(s).");
                        continue;
                    }
                    else if (imagePathsGroup.Count() > 1)
                    {
                        Log($"WARNING: multiple input files detected for '{textureName}' ({string.Join(", ", imagePathsGroup)}). Skipping files.");
                        continue;
                    }
                    // NOTE: Texture dimensions (which must be multiples of 16) are checked later, in CreateTextureFromImage.


                    var filePath = imagePathsGroup.Single();
                    var isExistingImage = wadTextureNames.Contains(textureName.ToLowerInvariant());
                    var isSupportedFileType = ImageReading.IsSupported(filePath);

                    // For files that are not directly supported, we'll include their extension when looking up conversion settings:
                    (var textureSettings, var lastSettingsChangeTime) = wadMakingSettings.GetTextureSettings(isSupportedFileType ? textureName : Path.GetFileName(filePath));
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
                            Directory.CreateDirectory(conversionOutputDirectory);

                            var outputFilePaths = ExternalConversion.ExecuteConversionCommand(textureSettings.Converter, textureSettings.ConverterArguments, filePath, imageFilePath, Log);
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
                            Log($"Updated texture '{textureName}' (from '{filePath}').");
                        }
                        else
                        {
                            // Add new texture:
                            wad.Textures.Add(texture);
                            wadTextureNames.Add(textureName);
                            texturesAdded += 1;
                            Log($"Added texture '{textureName}' (from '{filePath}').");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: failed to build '{filePath}': {ex.GetType().Name}: '{ex.Message}'.");
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
                            Log($"Removed texture '{textureName}'.");
                        }
                    }
                }

                // Finally, save the wad file:
                Directory.CreateDirectory(Path.GetDirectoryName(outputWadFilePath));
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
                    Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            if (updateExistingWad)
                Log($"Updated {outputWadFilePath} from {inputDirectory}: added {texturesAdded}, updated {texturesUpdated} and removed {texturesRemoved} textures, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
            else
                Log($"Created {outputWadFilePath}, with {texturesAdded} textures from {inputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        // Wad extraction:
        static Image<Rgba32> DecalTextureToImage(Texture texture, int mipmap = 0)
        {
            var decalColor = texture.Palette[255];

            var image = new Image<Rgba32>(texture.Width, texture.Height);
            for (int y = 0; y < image.Height; y++)
            {
                var rowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    var paletteIndex = texture.ImageData[y * texture.Width + x];
                    rowSpan[x] = new Rgba32(decalColor.R, decalColor.G, decalColor.B, paletteIndex);
                }
            }

            return image;
        }

        static Image<Rgba32> TextureToImage(Texture texture, int mipmap = 0)
        {
            var hasColorKey = texture.Name.StartsWith("{");

            var image = new Image<Rgba32>(texture.Width, texture.Height);
            for (int y = 0; y < image.Height; y++)
            {
                var rowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    var paletteIndex = texture.ImageData[y * texture.Width + x];
                    if (paletteIndex == 255 && hasColorKey)
                    {
                        rowSpan[x] = new Rgba32(0, 0, 0, 0);
                    }
                    else
                    {
                        rowSpan[x] = texture.Palette[paletteIndex];
                    }
                }
            }

            return image;
        }


        // Wad making:
        // TODO: Really allow all characters in this range? Aren't there some characters that may cause trouble (in .map files, for example, such as commas, parenthesis, etc.?)
        static bool IsValidTextureName(string name) => name.All(c => c > 0 && c < 256 && c != ' ');

        static string GetTextureName(string path)=> Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

        static IEnumerable<string> GetMipmapFilePaths(string path)
        {
            for (int mipmap = 1; mipmap <= 3; mipmap++)
                yield return Path.ChangeExtension(path, $".mipmap{mipmap}{Path.GetExtension(path)}");
        }

        static Texture CreateTextureFromImage(string path, string textureName, TextureSettings textureSettings, bool isDecalsWad)
        {
            // Load the main texture image, and any available mipmap images:
            using (var images = new DisposableList<Image<Rgba32>>(GetMipmapFilePaths(path).Prepend(path)
                .Select(imagePath => File.Exists(imagePath) ? ImageReading.ReadImage(imagePath) : null)))
            {
                // Verify image sizes:
                if (images[0].Width % 16 != 0 || images[0].Height % 16 != 0)
                    throw new InvalidDataException($"Texture '{path}' width or height is not a multiple of 16.");

                for (int i = 1; i < images.Count; i++)
                    if (images[i] != null && (images[i].Width != images[0].Width >> i || images[i].Height != images[0].Height >> i))
                        throw new InvalidDataException($"Mipmap {i} for texture '{path}' width or height does not match texture size.");

                if (isDecalsWad)
                    return CreateDecalTexture(textureName, images.ToArray(), textureSettings);


                var filename = Path.GetFileName(path);
                var isTransparentTexture = filename.StartsWith("{");
                var isAnimatedTexture = AnimatedTextureNameRegex.IsMatch(filename);
                var isWaterTexture = filename.StartsWith("!");

                // Create a suitable palette, taking special texture types into account:
                var transparencyThreshold = isTransparentTexture ? Clamp(textureSettings.TransparencyThreshold ?? 128, 0, 255) : 0;
                Func<Rgba32, bool> isTransparentPredicate = null;
                if (textureSettings.TransparencyColor != null)
                {
                    var transparencyColor = textureSettings.TransparencyColor.Value;
                    isTransparentPredicate = color => color.A < transparencyThreshold || (color.R == transparencyColor.R && color.G == transparencyColor.G && color.B == transparencyColor.B);
                }
                else
                {
                    isTransparentPredicate = color => color.A < transparencyThreshold;
                }

                var colorHistogram = ColorQuantization.GetColorHistogram(images.Where(image => image != null), isTransparentPredicate);
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
                    var fogIntensity = new Rgba32((byte)Clamp(textureSettings.WaterFogColor?.A ?? (int)((1f - GetBrightness(fogColor)) * 255), 0, 255), 0, 0);

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
                        images[i] = images[0].Clone(context => context.Resize(images[0].Width >> i, images[0].Height >> i));
                }

                // Create texture data:
                var textureData = images
                    .Select(image => CreateTextureData(image, palette, colorIndexMappingCache, textureSettings, isTransparentPredicate, disableDithering: isAnimatedTexture))
                    .ToArray();

                return Texture.CreateMipmapTexture(
                    name: textureName,
                    width: images[0].Width,
                    height: images[0].Height,
                    imageData: textureData[0],
                    palette: palette,
                    mipmap1Data: textureData[1],
                    mipmap2Data: textureData[2],
                    mipmap3Data: textureData[3]);
            }
        }

        static Texture CreateDecalTexture(string name, Image<Rgba32>[] images, TextureSettings textureSettings)
        {
            // Create any missing mipmaps (this does not affect the palette, so it can be done up-front):
            for (int i = 1; i < images.Length; i++)
            {
                if (images[i] == null)
                    images[i] = images[0].Clone(context => context.Resize(images[0].Width >> i, images[0].Height >> i));
            }

            // The last palette color determines the color of the decal. All other colors are irrelevant - palette indexes are treated as alpha values instead.
            var decalColor = textureSettings.DecalColor ?? ColorQuantization.GetAverageColor(ColorQuantization.GetColorHistogram(images, color => color.A == 0));
            var palette = Enumerable.Range(0, 255)
                .Select(i => new Rgba32((byte)i, (byte)i, (byte)i))
                .Append(decalColor)
                .ToArray();

            var textureData = images
                .Select(CreateDecalTextureData)
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: name,
                width: images[0].Width,
                height: images[0].Height,
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
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = image.GetPixelRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var color = rowSpan[x];
                        data[y * image.Width + x] = getPaletteIndex(color);
                    }
                }
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
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = image.GetPixelRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var color = rowSpan[x];
                        textureData[y * image.Width + x] = (byte)getColorIndex(color);
                    }
                }
                return textureData;
            }
        }

        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));


        static void Log(string message)
        {
            Console.WriteLine(message);
            LogFile?.WriteLine(message);
        }
    }
}
