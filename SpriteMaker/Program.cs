﻿using Shared;
using Shared.FileFormats;
using Shared.Sprites;
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

namespace SpriteMaker
{
    class ProgramSettings
    {
        // General settings:
        public bool IncludeSubDirectories { get; set; }         // -subdirs         also processes files in sub-directories (applies to both sprite building and extracting)

        // Build settings:
        public bool RemoveSpritesWithoutSource { get; set; }    // -remove          removes sprite files that do not have a source images (be careful with this!)

        // Extract settings:
        public bool Extract { get; set; }                       // -extract         extracts all sprites in the input directory (this is also enabled if the input file is a .spr file)
        public bool ExtractAsSpriteSheet { get; set; }          // -spritesheet     extracts multi-frame sprites as spritesheets instead of image sequences
        public bool OverwriteExistingFiles { get; set; }        // -overwrite       extract mode only, enables overwriting of existing image files (off by default)
        public bool ExtractAsGif { get; set; }                  // -gif             extract sprites as (animated) gif files

        // Other settings:
        public string InputPath { get; set; }                   // An image or sprite file, or a directory full of images (or sprites, if -extract is set)
        public string OutputPath { get; set; }                  // Output sprite or image path, or output directory path

        public bool DisableFileLogging { get; set; }            // -nologfile       disables logging to a file (parent-directory\spritemaker.log)
    }

    /*
    Usage:

    SpriteMaker.exe input (output)
        If output is not specified, then it will be set to a value that depends on the input and the kind of input.
        If input is:
        - a sprite file: SpriteMaker will 'extract' the sprite file into a png file (or files, for multi-frame pngs, depending on command-line option -nospritesheet)
        - an image file: SpriteMaker will turn it into a sprite (if image name contains a .N part, then all related frame images are also used to create a multi-frame sprite)
        - a directory: SpriteMaker will turn all images in the folder into sprites (unless the -extract command-line option is specified, then it'll create images from all sprite files)
    */
    class Program
    {
        static TextWriter LogFile;


        static void Main(string[] args)
        {
            try
            {
                Log($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");

                var settings = ParseArguments(args);
                if (settings.Extract)
                {
                    if (!string.IsNullOrEmpty(Path.GetExtension(settings.InputPath)))
                        ExtractSprite(settings.InputPath, settings.OutputPath, settings.ExtractAsSpriteSheet, settings.OverwriteExistingFiles);
                    else
                        ExtractSprites(settings.InputPath, settings.OutputPath, settings.ExtractAsSpriteSheet, settings.OverwriteExistingFiles, settings.IncludeSubDirectories);
                }
                else
                {
                    if (!settings.DisableFileLogging)
                    {
                        var logFilePath = Path.Combine(Path.GetDirectoryName(settings.InputPath), $"spritemaker - {Path.GetFileName(settings.InputPath)}.log");
                        LogFile = new StreamWriter(logFilePath, false, Encoding.UTF8);
                        LogFile.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");
                    }

                    if (!string.IsNullOrEmpty(Path.GetExtension(settings.InputPath)))
                        MakeSprite(settings.InputPath, settings.OutputPath);
                    else
                        MakeSprites(settings.InputPath, settings.OutputPath, settings.IncludeSubDirectories, settings.RemoveSpritesWithoutSource);
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
                    case "-subdirs": settings.IncludeSubDirectories = true; break;
                    case "-extract": settings.Extract = true; break;
                    case "-spritesheet": settings.ExtractAsSpriteSheet = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;
                    case "-nologfile": settings.DisableFileLogging = true; break;
                    default: throw new ArgumentException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new ArgumentException("Missing input path (image or sprite file, or folder) argument.");

            if (paths[0].EndsWith(".spr"))
                settings.Extract = true;


            if (settings.Extract)
            {
                // Sprite extraction requires a spr file path or a directory:
                settings.InputPath = args[index++];

                if (index < args.Length)
                {
                    settings.OutputPath = args[index++];
                }
                else
                {
                    // \sprites\fire.spr    ==> \sprites\extracted (\fire.png)
                    // \sprites             ==> \sprites_extracted
                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), "extracted");
                    else
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), Path.GetFileNameWithoutExtension(settings.InputPath) + "_extracted");
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
                    // \images\fire.png     ==> \images\sprites (\fire.spr)
                    // \images              ==> \images_sprites
                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), "sprites");
                    else
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), Path.GetFileNameWithoutExtension(settings.InputPath) + "_sprites");
                }
            }

            return settings;
        }


        static void ExtractSprites(string inputDirectory, string outputDirectory, bool extractAsSpritesheet, bool overwriteExistingFiles, bool includeSubDirectories)
        {
            throw new NotImplementedException();
        }

        static void ExtractSprite(string inputPath, string outputDirectory, bool extractAsSpritesheet, bool overwriteExistingFiles)
        {
            throw new NotImplementedException();
        }


        static void MakeSprites(string inputDirectory, string outputDirectory, bool includeSubDirectories, bool removeSpritesWithoutSource)
        {
            var stopwatch = Stopwatch.StartNew();

            var spritesAdded = 0;
            var spritesUpdated = 0;
            var spritesRemoved = 0;

            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory);
            var conversionOutputDirectory = Path.Combine(inputDirectory, Guid.NewGuid().ToString());

            var existingSpriteNames = new HashSet<string>();
            if (Directory.Exists(outputDirectory))
            {
                existingSpriteNames = Directory.EnumerateFiles(outputDirectory, "*", includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Select(GetSpriteName)
                    .ToHashSet();
            }
            Directory.CreateDirectory(outputDirectory);

            // Multiple files can map to the same sprite, due to different extensions, filename suffixes and upper/lower-case differences.
            // We'll group files by sprite name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory, "*", includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToHashSet();
            var spriteImagePaths = allInputDirectoryFiles
                .Where(path => ImageReading.IsSupported(path) || spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings.Converter != null)
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .GroupBy(path => GetSpriteName(path));

            try
            {
                foreach (var imagePathsGroup in spriteImagePaths)
                {
                    var spriteName = imagePathsGroup.Key;
                    var isExistingSprite = existingSpriteNames.Contains(spriteName);
                    var imagePathsAndSettings = imagePathsGroup
                        .Select(path =>
                        {
                            var isSupportedFileType = ImageReading.IsSupported(path);
                            return (
                                path,
                                isSupportedFileType,
                                filenameSettings: SpriteFilenameSettings.FromFilename(path),
                                spriteSettings: spriteMakingSettings.GetSpriteSettings(isSupportedFileType ? spriteName : Path.GetFileName(path)));
                        })
                        .OrderBy(file => file.filenameSettings.FrameNumber)
                        .ToArray();

                    if (imagePathsAndSettings.Any(file => !file.isSupportedFileType && file.spriteSettings.settings.ConverterArguments == null))
                    {
                        Log($"WARNING: some input files for '{spriteName}' are missing converter arguments. Skipping sprite.");
                        continue;
                    }
                    else if (imagePathsGroup.Count() > 1 && imagePathsAndSettings.Any(file => file.filenameSettings.FrameNumber == null))
                    {
                        Log($"WARNING: not all input files for '{spriteName}' contain a frame number ({string.Join(", ", imagePathsGroup)}). Skipping sprite.");
                        continue;
                    }
                    else if (imagePathsGroup.Count() > 1 && imagePathsAndSettings.Any(file => file.filenameSettings.SpritesheetTileSize != null))
                    {
                        Log($"WARNING: some input files for '{spriteName}' have a frame number and are marked as spritesheet, which is not supported. Skipping sprite.");
                        continue;
                    }

                    try
                    {
                        // TODO: If we're in 'update' mode:
                        //       IF the target sprite exists
                        //          AND it's updated more recently than all of our images
                        //          AND it's updated more recently than all of our image settings,
                        //          AND its orientation and render mode match our sprite settings,
                        //       THEN skip this sprite!

                        using (var frameImages = new DisposableList<FrameImage>())
                        {
                            foreach (var file in imagePathsAndSettings)
                            {
                                // Do we need to convert this image?
                                var imageFilePath = file.path;
                                var spriteSettings = file.spriteSettings.settings;
                                if (spriteSettings.Converter != null)
                                {
                                    if (spriteSettings.ConverterArguments == null)
                                        throw new InvalidDataException($"Unable to convert '{file.path}': missing converter arguments.");

                                    imageFilePath = Path.Combine(conversionOutputDirectory, Path.GetFileNameWithoutExtension(file.path) + ".png");
                                    Directory.CreateDirectory(conversionOutputDirectory);

                                    ExecuteConversionCommand(
                                        spriteSettings.Converter,
                                        spriteSettings.ConverterArguments,
                                        file.path,
                                        imageFilePath);
                                }

                                // Load images (and cut up spritesheets into separate frame images):
                                var image = ImageReading.ReadImage(imageFilePath);
                                if (file.filenameSettings.SpritesheetTileSize is Size tileSize)
                                {
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

                            // Sprite settings:
                            var firstFile = imagePathsAndSettings.First();
                            var spriteOrientation = firstFile.filenameSettings.Orientation ?? firstFile.spriteSettings.settings.SpriteOrientation ?? SpriteOrientation.Parallel;
                            var spriteRenderMode = firstFile.filenameSettings.RenderMode ?? firstFile.spriteSettings.settings.SpriteRenderMode ?? SpriteRenderMode.Additive;

                            var sprite = CreateSpriteFromImages(frameImages, spriteName, spriteOrientation, spriteRenderMode);
                            sprite.Save(Path.Combine(outputDirectory, spriteName + ".spr"));

                            // TODO: Or spritesUpdated!
                            spritesAdded += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: Failed to build '{spriteName}': {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }

                if (removeSpritesWithoutSource)
                {
                    // TODO: It would be safer if SpriteMaker would keep track of all the sprites that came from a certain folder,
                    //       so it knows which sprites it can delete -- otherwise it might wipe a lot of sprites if you accidentally use the wrong output directory!
                    var newSpriteNames = spriteImagePaths
                        .Select(group => group.Key)
                        .ToHashSet();
                    foreach (var spriteName in existingSpriteNames)
                    {
                        if (!newSpriteNames.Contains(spriteName))
                            ;   // TODO: Remove sprite???
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

            Log($"Updated {outputDirectory} from {inputDirectory}: added {spritesAdded}, updated {spritesUpdated} and removed {spritesRemoved} sprites, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        static void MakeSprite(string inputPath, string outputPath)
        {
            throw new NotImplementedException();
        }


        // Sprite making:
        static string GetSpriteName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);

            var dotIndex = name.IndexOf('.');
            if (dotIndex >= 0)
                name = name.Substring(0, dotIndex);

            return name.ToLowerInvariant();
        }

        static void ExecuteConversionCommand(string converter, string converterArguments, string inputPath, string outputPath)
        {
            var arguments = converterArguments.Replace("{input}", inputPath).Replace("{output}", outputPath).Trim();
            Log($"Executing conversion command: '{converter} {arguments}'.");

            var startInfo = new ProcessStartInfo(converter, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) => Log($"    INFO: {e.Data}");
                process.ErrorDataReceived += (sender, e) => Log($"    ERROR: {e.Data}");
                process.Start();
                if (!process.WaitForExit(10_000))
                    throw new TimeoutException("Conversion command did not finish within 10 seconds, .");

                Log($"Conversion command finished with exit code: {process.ExitCode}.");
            }
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

        static Sprite CreateSpriteFromImages(IList<FrameImage> frameImages, string spriteName, SpriteOrientation spriteOrientation, SpriteRenderMode spriteRenderMode)
        {
            if (spriteRenderMode == SpriteRenderMode.IndexAlpha)
                return CreateIndexAlphaSpriteFromImages(frameImages, spriteName, spriteOrientation);

            // Create a single color histogram from all frame images:
            var colorHistogram = new Dictionary<Rgba32, int>();
            var isAlphaTest = spriteRenderMode == SpriteRenderMode.AlphaTest;
            foreach (var frameImage in frameImages)
                ColorQuantization.UpdateColorHistogram(colorHistogram, frameImage.Image, MakeTransparencyPredicate(frameImage));

            // Create a suitable palette, taking sprite render mode into account:
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


            // Create the sprite and its frames:
            var spriteWidth = frameImages.Max(frameImage => frameImage.Image.Width);
            var spriteHeight = frameImages.Max(frameImage => frameImage.Image.Height);
            var isAnimatedSprite = frameImages.Count() > 1;

            var sprite = Sprite.CreateSprite(spriteOrientation, spriteRenderMode, spriteWidth, spriteHeight, palette);
            foreach (var frameImage in frameImages)
            {
                var image = frameImage.Image;
                sprite.Frames.Add(new Frame {
                    FrameGroup = 0,
                    FrameOriginX = -(frameImage.Settings.FrameOrigin?.X ?? (image.Width / 2)),
                    FrameOriginY = frameImage.Settings.FrameOrigin?.Y ?? (image.Height / 2),
                    FrameWidth = (uint)image.Width,
                    FrameHeight = (uint)image.Height,
                    ImageData = CreateFrameImageData(frameImage.Image, palette, colorIndexMappingCache, frameImage.Settings, MakeTransparencyPredicate(frameImage), disableDithering: isAnimatedSprite),
                });
            }
            return sprite;


            Func<Rgba32, bool> MakeTransparencyPredicate(FrameImage frameImage)
            {
                var transparencyThreshold = isAlphaTest ? Clamp(frameImage.Settings.AlphaTestTransparencyThreshold ?? 128, 0, 255) : 0;
                if (frameImage.Settings.AlphaTestTransparencyColor is Rgba32 transparencyColor)
                    return color => color.A < transparencyThreshold || (color.R == transparencyColor.R && color.G == transparencyColor.G && color.B == transparencyColor.B);

                return color => color.A < transparencyThreshold;
            }
        }

        static Sprite CreateIndexAlphaSpriteFromImages(IList<FrameImage> frameImages, string spriteName, SpriteOrientation spriteOrientation)
        {
            Rgba32 decalColor;
            if (frameImages.First().Settings.IndexAlphaColor is Rgba32 indexAlphaColor)
            {
                decalColor = indexAlphaColor;
            }
            else
            {
                var colorHistogram = ColorQuantization.GetColorHistogram(frameImages.Select(frameImage => frameImage.Image), color => color.A == 0);
                decalColor = ColorQuantization.GetAverageColor(colorHistogram);
            }
            var palette = Enumerable.Range(0, 255)
                .Select(i => new Rgba32((byte)i, (byte)i, (byte)i))
                .Append(decalColor)
                .ToArray();

            var spriteWidth = frameImages.Max(frameImage => frameImage.Image.Width);
            var spriteHeight = frameImages.Max(frameImage => frameImage.Image.Height);
            var sprite = Sprite.CreateSprite(spriteOrientation, SpriteRenderMode.IndexAlpha, spriteWidth, spriteHeight, palette);
            foreach (var frameImage in frameImages)
            {
                var mode = frameImage.Settings.IndexAlphaTransparencySource ?? IndexAlphaTransparencySource.AlphaChannel;
                var getPaletteIndex = (mode == IndexAlphaTransparencySource.AlphaChannel) ? (Func<Rgba32, byte>)(color => color.A) :
                                                                                            (Func<Rgba32, byte>)(color => (byte)((color.R + color.G + color.B) / 3));

                var image = frameImage.Image;
                var frame = new Frame {
                    FrameGroup = 0,
                    FrameOriginX = -(frameImage.Settings.FrameOrigin?.X ?? (image.Width / 2)),
                    FrameOriginY = frameImage.Settings.FrameOrigin?.Y ?? (image.Height / 2),
                    FrameWidth = (uint)image.Width,
                    FrameHeight = (uint)image.Height,
                    ImageData = new byte[image.Width * image.Height],
                };

                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = image.GetPixelRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var color = rowSpan[x];
                        frame.ImageData[y * image.Width + x] = getPaletteIndex(color);
                    }
                }

                sprite.Frames.Add(frame);
            }
            return sprite;
        }

        static byte[] CreateFrameImageData(
            Image<Rgba32> image,
            Rgba32[] palette,
            IDictionary<Rgba32, int> colorIndexMappingCache,
            SpriteSettings spriteSettings,
            Func<Rgba32, bool> isTransparent,
            bool disableDithering)
        {
            var getColorIndex = ColorQuantization.CreateColorIndexLookup(palette, colorIndexMappingCache, isTransparent);

            var ditheringAlgorithm = spriteSettings.DitheringAlgorithm ?? (disableDithering ? DitheringAlgorithm.None : DitheringAlgorithm.FloydSteinberg);
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None:
                    return ApplyPaletteWithoutDithering();

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(image, palette, getColorIndex, spriteSettings.DitherScale ?? 0.75f, isTransparent);
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

        // TODO: Move this to a common place in Shared -- it's duplicated 3 times now!
        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

        static void Log(string message)
        {
            Console.WriteLine(message);
            LogFile?.WriteLine(message);
        }
    }
}