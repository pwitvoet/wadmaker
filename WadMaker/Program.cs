using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WadMaker
{
    class Settings
    {
        public bool FullRebuild { get; set; }
        public bool Extract { get; set; }

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
                    ExtractWad(settings.WadPath, settings.OutputDirectory);
                }
                else
                {
                    BuildWad(settings.InputDirectory, settings.WadPath, settings.FullRebuild);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.GetType().Name}: '{ex.Message}'.");
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
        static void ExtractWad(string inputWadPath, string outputDirectory)
        {
            var stopwatch = Stopwatch.StartNew();

            var wad = Wad.Load(inputWadPath);
            Directory.CreateDirectory(outputDirectory);
            foreach (var texture in wad.Textures)
            {
                using (var image = TextureToBitmap(texture))
                    image.Save(Path.Combine(outputDirectory, texture.Name + ".png"), ImageFormat.Png);
            }

            Console.WriteLine($"Extracted {wad.Textures.Count} textures from {inputWadPath} to {outputDirectory}, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        // TODO: IMPLEMENT THIS!!!
        static void BuildWad(string inputDirectory, string outputWadPath, bool fullRebuild)
        {
            throw new NotImplementedException("TODO");
        }


        static Bitmap TextureToBitmap(Texture texture)
        {
            if (texture.Name.StartsWith('{'))
                return ColorKeyedTextureToBitmap(texture);

            var buffer = new byte[texture.Width * texture.Height * 3];
            var index = 0;
            for (int i = 0; i < texture.ImageData.Length; i++)
            {
                var color = texture.Palette[texture.ImageData[i]];
                buffer[index++] = color.B;
                buffer[index++] = color.G;
                buffer[index++] = color.R;
            }

            var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format24bppRgb);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);
            bitmap.UnlockBits(bitmapData);
            return bitmap;
        }

        static Bitmap ColorKeyedTextureToBitmap(Texture texture)
        {
            var buffer = new byte[texture.Width * texture.Height * 4];
            var index = 0;
            for (int i = 0; i < texture.ImageData.Length; i++)
            {
                var paletteIndex = texture.ImageData[i];
                if (paletteIndex == 255)
                {
                    index += 4;
                }
                else
                {
                    var color = texture.Palette[texture.ImageData[i]];
                    buffer[index++] = color.B;
                    buffer[index++] = color.G;
                    buffer[index++] = color.R;
                    buffer[index++] = 255;
                }
            }

            var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);
            bitmap.UnlockBits(bitmapData);
            return bitmap;
        }
    }
}
