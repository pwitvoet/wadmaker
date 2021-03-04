﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using WadMaker.Drawing;

namespace WadMaker
{
    class Settings
    {
        public bool FullRebuild { get; set; }           // -full: forces a full rebuild instead of an incremental one
        public bool Extract { get; set; }               // -extract: extracts textures from a wad file instead of building a wad file
        public bool ExtractMipmaps { get; set; }        // -mipmaps: also extracts mipmaps

        public string InputDirectory { get; set; }      // Build mode only
        public string WadPath { get; set; }             // Wad path (output in build mode, input in extract mode).
        public string OutputDirectory { get; set; }     // Extract mode only
    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");

                var settings = ParseArguments(args);
                if (settings.Extract)
                {
                    ExtractWad(settings.WadPath, settings.OutputDirectory, settings.ExtractMipmaps);
                }
                else
                {
                    MakeWad(settings.InputDirectory, settings.WadPath, settings.FullRebuild);
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

            var index = 0;
            while (index < args.Length && args[index].StartsWith('-'))
            {
                var arg = args[index++];
                switch (arg)
                {
                    case "-full": settings.FullRebuild = true; break;
                    case "-extract": settings.Extract = true; break;
                    case "-mipmaps": settings.ExtractMipmaps = true; break;
                    default: throw new ArgumentException($"Unknown argument: '{arg}'.");
                }
            }

            if (settings.Extract)
            {
                if (index < args.Length)
                    settings.WadPath = args[index++];
                else
                    throw new ArgumentException("Missing input wad argument.");

                if (index < args.Length)
                    settings.OutputDirectory = args[index++];
                else
                    settings.OutputDirectory = Path.Combine(Path.GetDirectoryName(settings.WadPath), Path.GetFileNameWithoutExtension(settings.WadPath));
            }
            else
            {
                if (index < args.Length)
                    settings.InputDirectory = args[index++];
                else
                    throw new ArgumentException("Missing input directory argument.");

                if (index < args.Length)
                    settings.WadPath = args[index++];
                else
                    settings.WadPath = $"{Path.GetFileName(settings.InputDirectory)}.wad";

                if (!Path.IsPathRooted(settings.WadPath))
                    settings.WadPath = Path.Combine(Path.GetDirectoryName(settings.InputDirectory), settings.WadPath);
            }

            return settings;
        }

        // TODO: What if dir already exists? ...ask to overwrite files? maybe add a -force cmd flag?
        // TODO: Also create a wadmaker.config file, if the wad contained fonts or simple images (mipmap textures are the default behavior, so those don't need a config,
        //       unless the user wants to create a wad file and wants different settings for those images such as different dithering, etc.)
        static void ExtractWad(string inputWadPath, string outputDirectory, bool extractMipmaps)
        {
            var stopwatch = Stopwatch.StartNew();

            var wad = Wad.Load(inputWadPath);
            Directory.CreateDirectory(outputDirectory);
            foreach (var texture in wad.Textures)
            {
                var maxMipmap = extractMipmaps ? 4 : 1;
                for (int mipmap = 0; mipmap < maxMipmap; mipmap++)
                {
                    try
                    {
                        using (var image = TextureToBitmap(texture, mipmap))
                        {
                            image.Save(Path.Combine(outputDirectory, texture.Name + $"{(mipmap > 0 ? ".mipmap" + mipmap : "")}.png"), ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: failed to extract '{texture.Name}'{(mipmap > 0 ? $" (mipmap {mipmap})" : "")}: {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }
            }

            Console.WriteLine($"Extracted {wad.Textures.Count} textures from {inputWadPath} to {outputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        // TODO: Add support for more image file formats!
        static void MakeWad(string inputDirectory, string outputWadPath, bool fullRebuild)
        {
            var stopwatch = Stopwatch.StartNew();

            var texturesAdded = 0;
            var texturesUpdated = 0;
            var texturesRemoved = 0;

            var updateExistingWad = !fullRebuild && File.Exists(outputWadPath);
            var wad = updateExistingWad ? Wad.Load(outputWadPath) : new Wad();
            var lastWadUpdateTime = updateExistingWad ? new FileInfo(outputWadPath).LastWriteTimeUtc : (DateTime?)null;
            var wadTextureNames = wad.Textures.Select(texture => texture.Name.ToLowerInvariant()).ToHashSet();

            // Multiple files can map to the same texture, due to different extensions and upper/lower-case differences.
            // We'll group files by texture name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory).ToHashSet();
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
                    var texture = CreateTextureFromImage(filePath);

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
            wad.Save(outputWadPath);

            if (updateExistingWad)
                Console.WriteLine($"Updated {outputWadPath} from {inputDirectory}: added {texturesAdded}, updated {texturesUpdated} and removed {texturesRemoved} textures, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
            else
                Console.WriteLine($"Created {outputWadPath}, with {texturesAdded} textures from {inputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


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

        static Texture CreateTextureFromImage(string path)
        {
            // First load all input images (mipmaps are optional, missing ones will be generated automatically):
            var imageCanvas = CanvasFromFile(path);
            if (imageCanvas.Width % 16 != 0 || imageCanvas.Height % 16 != 0)
                throw new InvalidDataException($"Texture '{path}' width or height is not a multiple of 16.");

            var mipmapCanvases = GetMipmapFilePaths(path)
                .Select(mipmapPath => File.Exists(mipmapPath) ? CanvasFromFile(mipmapPath) : null)
                .ToArray();

            // Then quantize the images (together, because they'll be using the same palette):
            var hasTransparency = Path.GetFileName(path).StartsWith("{");
            var quantizedCanvases = ColorQuantization.CreateIndexedCanvases(mipmapCanvases.Where(mipmap => mipmap != null).Prepend(imageCanvas), hasTransparency).ToArray();

            var mipmapIndex = 1;
            return Texture.CreateMipmapTexture(
                name: Path.GetFileNameWithoutExtension(path),
                width: imageCanvas.Width,
                height: imageCanvas.Height,
                imageData: GetBuffer(quantizedCanvases[0]),
                palette: quantizedCanvases[0].Palette,
                mipmap1Data: GetBuffer(mipmapCanvases[0] != null ? quantizedCanvases[mipmapIndex++] : CreateMipmap(quantizedCanvases[0], 2)),
                mipmap2Data: GetBuffer(mipmapCanvases[1] != null ? quantizedCanvases[mipmapIndex++] : CreateMipmap(quantizedCanvases[0], 4)),
                mipmap3Data: GetBuffer(mipmapCanvases[2] != null ? quantizedCanvases[mipmapIndex++] : CreateMipmap(quantizedCanvases[0], 8)));
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
