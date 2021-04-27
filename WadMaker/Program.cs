using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using WadMaker.Drawing;

namespace WadMaker
{
    class Settings
    {
        // Build settings:
        public bool FullRebuild { get; set; }               // -full:       forces a full rebuild instead of an incremental one
        public bool IncludeSubDirectories { get; set; }     // -subdirs:    also include images in sub-directories

        // Extract settings:
        public bool Extract { get; set; }                   //              Texture extraction mode is enabled when the first argument (path) is a wad or bsp file.
        public bool ExtractMipmaps { get; set; }            // -mipmaps:    also extract mipmaps
        public bool OverwriteExistingFiles { get; set; }    // -overwrite:  extract mode only, enables overwriting of existing image files (off by default)

        // Other settings:
        public string InputDirectory { get; set; }          // Build mode only
        public string FilePath { get; set; }                // Wad or bsp path (output in build mode, input in extract mode).
        public string OutputDirectory { get; set; }         // Extract mode only
    }

    class Program
    {
        static Regex AnimatedTextureNameRegex = new Regex(@"^\+[0-9A-J]");


        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");

                var settings = ParseArguments(args);
                if (settings.Extract)
                {
                    ExtractTextures(settings.FilePath, settings.OutputDirectory, settings.ExtractMipmaps, settings.OverwriteExistingFiles);
                }
                else
                {
                    MakeWad(settings.InputDirectory, settings.FilePath, settings.FullRebuild, settings.IncludeSubDirectories);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.GetType().Name}: '{ex.Message}'.");
                Console.WriteLine(ex.StackTrace);
            }
        }


        static Settings ParseArguments(string[] args)
        {
            var settings = new Settings();

            // First parse options:
            var index = 0;
            while (index < args.Length && args[index].StartsWith('-'))
            {
                var arg = args[index++];
                switch (arg)
                {
                    case "-full": settings.FullRebuild = true; break;
                    case "-subdirs": settings.IncludeSubDirectories = true; break;
                    case "-mipmaps": settings.ExtractMipmaps = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;
                    default: throw new ArgumentException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new ArgumentException("Missing input folder (for wad building) or file (for texture extraction) argument.");

            if (paths[0].EndsWith(".wad") || paths[0].EndsWith(".bsp"))
                settings.Extract = true;


            if (settings.Extract)
            {
                settings.FilePath = args[index++];

                if (index < args.Length)
                    settings.OutputDirectory = args[index++];
                else
                    settings.OutputDirectory = Path.Combine(Path.GetDirectoryName(settings.FilePath), Path.GetFileNameWithoutExtension(settings.FilePath));
            }
            else
            {
                settings.InputDirectory = args[index++];

                if (index < args.Length)
                    settings.FilePath = args[index++];
                else
                    settings.FilePath = $"{Path.GetFileName(settings.InputDirectory)}.wad";

                if (!Path.IsPathRooted(settings.FilePath))
                    settings.FilePath = Path.Combine(Path.GetDirectoryName(settings.InputDirectory), settings.FilePath);
            }

            return settings;
        }

        // TODO: What if dir already exists? ...ask to overwrite files? maybe add a -force cmd flag?
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
                            Console.WriteLine($"WARNING: {filePath} already exist. Skipping texture.");
                            continue;
                        }

                        using (var image = TextureToBitmap(texture, mipmap))
                        {
                            image.Save(filePath, ImageFormat.Png);
                            imageFilesCreated += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: failed to extract '{texture.Name}'{(mipmap > 0 ? $" (mipmap {mipmap})" : "")}: {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }
            }

            Console.WriteLine($"Extracted {imageFilesCreated} images from {textures.Count} textures from {inputFilePath} to {outputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        // TODO: Add support for more image file formats!
        static void MakeWad(string inputDirectory, string outputFilePath, bool fullRebuild, bool includeSubDirectories)
        {
            var stopwatch = Stopwatch.StartNew();

            var texturesAdded = 0;
            var texturesUpdated = 0;
            var texturesRemoved = 0;

            var updateExistingWad = !fullRebuild && File.Exists(outputFilePath);
            var wad = updateExistingWad ? Wad.Load(outputFilePath) : new Wad();
            var lastWadUpdateTime = updateExistingWad ? new FileInfo(outputFilePath).LastWriteTimeUtc : (DateTime?)null;
            var wadTextureNames = wad.Textures.Select(texture => texture.Name.ToLowerInvariant()).ToHashSet();

            // Multiple files can map to the same texture, due to different extensions and upper/lower-case differences.
            // We'll group files by texture name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory, "*", includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToHashSet();
            var textureImagePaths = allInputDirectoryFiles
                .Where(IsSupportedFiletype)
                .Where(path => !path.Contains(".mipmap"))
                .GroupBy(path => Path.GetFileNameWithoutExtension(path).ToLowerInvariant());

            // Check for new and updated images:
            foreach (var imagePaths in textureImagePaths)
            {
                var textureName = imagePaths.Key;
                if (!IsValidTextureName(textureName))
                {
                    Console.WriteLine($"WARNING: '{textureName}' is not a valid texture name ({string.Join(", ", imagePaths)}). Skipping file(s).");
                    continue;
                }
                else if (textureName.Length > 16)
                {
                    Console.WriteLine($"WARNING: The name '{textureName}' is too long ({string.Join(", ", imagePaths)}). Skipping file(s).");
                    continue;
                }
                else if (imagePaths.Count() > 1)
                {
                    Console.WriteLine($"WARNING: multiple input files detected for '{textureName}' ({string.Join(", ", imagePaths)}). Skipping files.");
                    continue;
                }
                // NOTE: Texture dimensions (which must be multiples of 16) are checked later, in CreateTextureFromImage.


                var filePath = imagePaths.Single();
                var isExistingImage = wadTextureNames.Contains(textureName);
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
                    if (!isImageUpdated)
                    {
                        //Console.WriteLine($"No modifications detected for '{textureName}' ({filePath}). Skipping file.");
                        continue;
                    }
                }

                try
                {
                    // Create texture from image:
                    var texture = CreateTextureFromImage(filePath, new TextureSettings { });    // TODO: Load texture-specific settings from files!

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
                        if (updateExistingWad)
                            Console.WriteLine($"Updated texture '{textureName}' (from {filePath}).");
                    }
                    else
                    {
                        // Add new texture:
                        wad.Textures.Add(texture);
                        wadTextureNames.Add(textureName);
                        texturesAdded += 1;
                        Console.WriteLine($"Added texture '{textureName}' (from {filePath}).");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: failed to build '{filePath}': {ex.GetType().Name}: '{ex.Message}'.");
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
                        wad.Textures.Remove(wad.Textures.First(texture => texture.Name == textureName));
                        texturesRemoved += 1;
                        Console.WriteLine($"Removed texture '{textureName}'.");
                    }
                }
            }

            // Finally, save the wad file:
            wad.Save(outputFilePath);

            if (updateExistingWad)
                Console.WriteLine($"Updated {outputFilePath} from {inputDirectory}: added {texturesAdded}, updated {texturesUpdated} and removed {texturesRemoved} textures, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
            else
                Console.WriteLine($"Created {outputFilePath}, with {texturesAdded} textures from {inputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        // Wad extraction:
        static Bitmap TextureToBitmap(Texture texture, int mipmap = 0)
        {
            var hasColorKey = texture.Name.StartsWith('{');

            var textureCanvas = CreateTextureCanvas(texture, mipmap);
            var bitmapCanvas = Canvas.Create(textureCanvas.Width, textureCanvas.Height, hasColorKey ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
            if (!hasColorKey)
            {
                textureCanvas.CopyTo(bitmapCanvas);
            }
            else
            {
                for (int y = 0; y < textureCanvas.Height; y++)
                {
                    for (int x = 0; x < textureCanvas.Width; x++)
                    {
                        var colorIndex = textureCanvas.GetIndex(x, y);
                        if (colorIndex != 255)
                            bitmapCanvas.SetPixel(x, y, textureCanvas.Palette[colorIndex]);
                    }
                }
            }
            return bitmapCanvas.CreateBitmap();
        }

        static IIndexedCanvas CreateTextureCanvas(Texture texture, int mipmap = 0)
        {
            var mipmapData = mipmap switch {
                0 => texture.ImageData,
                1 => texture.Mipmap1Data,
                2 => texture.Mipmap2Data,
                3 => texture.Mipmap3Data,
                _ => null
            };

            if (mipmapData == null)
                return null;

            var scale = 1 << mipmap;
            return IndexedCanvas.Create(texture.Width / scale, texture.Height / scale, PixelFormat.Format8bppIndexed, texture.Palette, mipmapData, texture.Width / scale);
        }



        // Wad making:
        // TODO: Really allow all characters in this range? Aren't there some characters that may cause trouble (in .map files, for example, such as commas, spaces, parenthesis, etc.?)
        static bool IsValidTextureName(string name) => name.All(c => c > 0 && c < 256);

        static bool IsSupportedFiletype(string path)
        {
            var extension = Path.GetExtension(path);
            return extension == ".png" || extension == ".bmp" || extension == ".jpg";
        }

        static IEnumerable<string> GetMipmapFilePaths(string path)
        {
            for (int mipmap = 1; mipmap <= 3; mipmap++)
                yield return Path.ChangeExtension(path, $".mipmap{mipmap}{Path.GetExtension(path)}");
        }

        static Texture CreateTextureFromImage(string path, TextureSettings textureSettings)
        {
            // First load all input images (mipmaps are optional, missing ones will be generated automatically):
            var imageCanvas = CanvasFromFile(path);
            if (imageCanvas.Width % 16 != 0 || imageCanvas.Height % 16 != 0)
                throw new InvalidDataException($"Texture '{path}' width or height is not a multiple of 16.");

            var mipmapCanvases = GetMipmapFilePaths(path)
                .Select(mipmapPath => File.Exists(mipmapPath) ? CanvasFromFile(mipmapPath) : null)
                .ToArray();

            // Are we dealing with a special texture (transparency, animation, water)?
            var filename = Path.GetFileName(path);
            var isTransparentTexture = filename.StartsWith("{");
            var isAnimatedTexture = AnimatedTextureNameRegex.IsMatch(filename);
            var isWaterTexture = filename.StartsWith('!');

            // Determine unique colors:
            var canvases = mipmapCanvases
                .Prepend(imageCanvas)
                .ToArray();
            var uniqueColors = canvases
                .Where(canvas => canvas != null)
                .SelectMany(canvas => canvas.GetColorHistogram().Keys)
                .ToHashSet();

            if (isTransparentTexture)
                uniqueColors.RemoveWhere(color => color.A < 128);

            // Create the palette (also taking the mipmaps into account, because they'll be sharing the palette):
            (var palette, var colorIndexMapping) = ColorQuantization.CreatePaletteAndColorIndexMapping(
                uniqueColors,
                isWaterTexture ? 254 : isTransparentTexture ? 255 : 256,
                textureSettings.QuantizationVolumeSelectionTreshold ?? 32);

            if (palette.Length < 256)
                palette = palette.Concat(Enumerable.Repeat(Color.FromArgb(0, 0, 0), 256 - palette.Length)).ToArray();

            // Palette handling for special textures:
            if (isTransparentTexture)
                palette[255] = Color.FromArgb(0, 0, 255);   // Make the transparent color deep blue, by convention.

            if (isWaterTexture)
            {
                // Fog color and intensity are stored in palette slots 3 and 4, so we'll have to move the colors at those slots:
                palette[254] = palette[3];
                palette[255] = palette[4];

                palette[3] = textureSettings.WaterFogColor ?? imageCanvas.GetAverageColor();
                palette[4] = Color.FromArgb(Math.Clamp(textureSettings.WaterFogIntensity ?? (int)((1f - palette[3].GetBrightness()) * 255), 0, 255), 0, 0);

                var affectedColors = colorIndexMapping
                    .Where(kv => kv.Value == 3 || kv.Value == 4)
                    .Select(kv => kv.Key)
                    .ToArray();
                foreach (var color in affectedColors)
                    colorIndexMapping[color] += 251;
            }


            // Finally, apply the palette (optionally using a dithering algorithm):
            var transparencyTreshold = textureSettings.TransparencyTreshold ?? (isTransparentTexture ? 128 : 0);
            var colorIndexLookup = ColorQuantization.CreateColorIndexLookup(palette, colorIndexMapping, color => color.A < transparencyTreshold);
            var resultCanvases = canvases
                .Select(canvas => (canvas != null) ? ApplyPalette(canvas, palette, colorIndexLookup, textureSettings, isAnimatedTexture) : null)
                .ToArray();

            return Texture.CreateMipmapTexture(
                name: Path.GetFileNameWithoutExtension(path),
                width: imageCanvas.Width,
                height: imageCanvas.Height,
                imageData: GetBuffer(resultCanvases[0]),
                palette: palette,
                mipmap1Data: GetBuffer(resultCanvases[1] != null ? resultCanvases[1] : CreateMipmap(resultCanvases[0], 2)),
                mipmap2Data: GetBuffer(resultCanvases[2] != null ? resultCanvases[2] : CreateMipmap(resultCanvases[0], 4)),
                mipmap3Data: GetBuffer(resultCanvases[3] != null ? resultCanvases[3] : CreateMipmap(resultCanvases[0], 8)));
        }

        // TODO: Without dithering there's still some potential for flickering, due to the palette being different!
        static IIndexedCanvas ApplyPalette(IReadableCanvas canvas, Color[] palette, Func<Color, int> colorIndexLookup, TextureSettings textureSettings, bool isAnimatedTexture)
        {
            // Do not apply dithering to animated textures, unless specifically requested, to avoid 'flickering':
            var ditheringAlgorithm = textureSettings.DitheringAlgorithm ?? (isAnimatedTexture ? DitheringAlgorithm.None : DitheringAlgorithm.FloydSteinberg);
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None:
                    return ApplyPaletteWithoutDithering(canvas, palette, colorIndexLookup);

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(canvas, palette, colorIndexLookup, textureSettings.MaxErrorDiffusion ?? 255);
            }
        }

        static IIndexedCanvas ApplyPaletteWithoutDithering(IReadableCanvas canvas, Color[] palette, Func<Color, int> colorIndexLookup)
        {
            var output = IndexedCanvas.Create(canvas.Width, canvas.Height, PixelFormat.Format8bppIndexed, palette);
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    var originalColor = canvas.GetPixel(x, y);
                    var paletteIndex = colorIndexLookup(originalColor);
                    output.SetIndex(x, y, paletteIndex);
                }
            }
            return output;
        }

        static IReadableCanvas CanvasFromFile(string path)
        {
            using (var bitmap = new Bitmap(path))
            {
                if (bitmap.PixelFormat.HasFlag(PixelFormat.Indexed))
                    return IndexedCanvas.Create(bitmap);

                return Canvas.Create(bitmap);
            }
        }

        // TODO: Take the average color of each block of pixels (or provide texture-specific options for this?)
        static IIndexedCanvas CreateMipmap(IIndexedCanvas canvas, int scale)
        {
            var mipmapCanvas = IndexedCanvas.Create(canvas.Width / scale, canvas.Height / scale, canvas.PixelFormat, canvas.Palette, stride: canvas.Width / scale);
            for (int y = 0; y < mipmapCanvas.Height; y++)
            {
                for (int x = 0; x < mipmapCanvas.Width; x++)
                {
                    mipmapCanvas.SetIndex(x, y, canvas.GetIndex(x * scale, y * scale));
                }
            }
            return mipmapCanvas;
        }

        static byte[] GetBuffer(IReadableCanvas canvas) => (canvas as IBufferCanvas)?.Buffer ?? canvas.CreateBuffer();
    }
}
