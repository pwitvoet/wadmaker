using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
                    BuildWad(settings.InputDirectory, settings.WadPath, settings.FullRebuild);
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
        // TODO: Also extract mipmaps? (texture.mipmap1.png?)
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

        // TODO: IMPLEMENT THIS!!!
        static void BuildWad(string inputDirectory, string outputWadPath, bool fullRebuild)
        {
            throw new NotImplementedException("TODO");
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
    }
}
