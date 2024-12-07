using Shared;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Diagnostics;

namespace WadMaker
{
    public static class TextureExtracting
    {
        // TODO: Also create a wadmaker.config file, if the wad contained fonts or simple images (mipmap textures are the default behavior, so those don't need a config,
        //       unless the user wants to create a wad file and wants different settings for those images such as different dithering, etc.)
        public static void ExtractTextures(string inputFilePath, string outputDirectory, bool extractMipmaps, bool overwriteExistingFiles, Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Extracting textures from '{inputFilePath}' and saving the result to '{outputDirectory}'.");

            var imageFilesCreated = 0;

            List<Texture> textures;
            if (Path.GetExtension(inputFilePath).ToLowerInvariant() == ".bsp")
            {
                logger.Log($"Loading bsp file: '{inputFilePath}'.");
                textures = Bsp.GetEmbeddedTextures(inputFilePath);
            }
            else
            {
                logger.Log($"Loading wad file: '{inputFilePath}'.");
                var wadFile = Wad.Load(inputFilePath, (index, name, exception) => logger.Log($"- Failed to load texture #{index} ('{name}'): {exception.GetType().Name}: '{exception.Message}'."));
                textures = wadFile.Textures;
            }

            CreateDirectory(outputDirectory);

            var isDecalsWad = Path.GetFileName(inputFilePath).ToLowerInvariant() == "decals.wad";
            foreach (var texture in textures)
            {
                var maxMipmap = (texture.Type == TextureType.MipmapTexture && extractMipmaps) ? 4 : 1;
                for (int mipmap = 0; mipmap < maxMipmap; mipmap++)
                {
                    try
                    {
                        var filePath = Path.Combine(outputDirectory, texture.Name + $"{(mipmap > 0 ? ".mipmap" + mipmap : "")}.png");
                        if (!overwriteExistingFiles && File.Exists(filePath))
                        {
                            logger.Log($"- WARNING: '{filePath}' already exist. Skipping texture.");
                            continue;
                        }

                        using (var image = isDecalsWad ? DecalTextureToImage(texture, mipmap) : TextureToImage(texture, mipmap))
                        {
                            if (image != null)
                            {
                                image.SaveAsPng(filePath);
                                imageFilesCreated += 1;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"- ERROR: failed to extract '{texture.Name}'{(mipmap > 0 ? $" (mipmap {mipmap})" : "")}: {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }
            }

            logger.Log($"Extracted {imageFilesCreated} images from {textures.Count} textures from '{inputFilePath}' to '{outputDirectory}', in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        static Image<Rgba32>? DecalTextureToImage(Texture texture, int mipmap = 0)
        {
            var imageData = texture.GetImageData(mipmap);
            if (imageData == null)
                return null;

            var width = texture.Width >> mipmap;
            var height = texture.Height >> mipmap;
            var decalColor = texture.Palette[255];

            var image = new Image<Rgba32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var paletteIndex = imageData[y * width + x];
                        rowSpan[x] = new Rgba32(decalColor.R, decalColor.G, decalColor.B, paletteIndex);
                    }
                }
            });

            return image;
        }

        static Image<Rgba32>? TextureToImage(Texture texture, int mipmap = 0)
        {
            var imageData = texture.GetImageData(mipmap);
            if (imageData == null)
                return null;

            var width = texture.Width >> mipmap;
            var height = texture.Height >> mipmap;
            var hasColorKey = texture.Name.StartsWith("{");

            var image = new Image<Rgba32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var paletteIndex = imageData[y * width + x];
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
            });

            return image;
        }


        static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
