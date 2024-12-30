using Shared;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Diagnostics;
using WadMaker.Settings;

namespace WadMaker
{
    public static class TextureExtracting
    {
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
                        var filePath = Path.Combine(outputDirectory, texture.Name + ".png");
                        var fileSettings = GetOutputFileTextureSettings(texture, mipmap);
                        filePath = WadMakingSettings.InsertTextureSettingsIntoFilename(filePath, fileSettings);

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

        public static void ExtractEmbeddedTexturesToWad(string inputBspFilePath, string outputWadFilePath, Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            logger.Log($"Extracting embedded textures from '{inputBspFilePath}' to '{outputWadFilePath}'.");

            var wad = new Wad();
            var embeddedTextures = Bsp.GetEmbeddedTextures(inputBspFilePath);
            wad.Textures.AddRange(embeddedTextures);

            // NOTE: The output file will be overwritten if it already exists:
            CreateDirectory(Path.GetDirectoryName(outputWadFilePath));
            wad.Save(outputWadFilePath);

            logger.Log($"Extracted {embeddedTextures.Count} textures from '{inputBspFilePath}' to '{outputWadFilePath}', in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
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
            var hasColorKey = TextureName.IsTransparent(texture.Name);

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

        static TextureSettings GetOutputFileTextureSettings(Texture texture, int mipmap)
        {
            if (mipmap > 0)
                return new TextureSettings { MipmapLevel = (MipmapLevel)mipmap };

            var settings = new TextureSettings { TextureType = texture.Type };
            if (TextureName.IsWater(texture.Name))
            {
                settings.WaterFogColor = new Rgba32(
                    texture.Palette[3].R,
                    texture.Palette[3].G,
                    texture.Palette[3].B,
                    texture.Palette[4].R);
            }
            return settings;
        }


        static void CreateDirectory(string? path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }
    }
}
