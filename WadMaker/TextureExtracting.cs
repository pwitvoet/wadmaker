using Shared;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Diagnostics;
using WadMaker.Settings;
using Shared.FileFormats;
using Shared.FileFormats.Indexed;

namespace WadMaker
{
    public class ExtractionSettings
    {
        public bool ExtractMipmaps { get; set; }
        public bool NoFullbrightMasks { get; set; }
        public bool OverwriteExistingFiles { get; set; }
        public bool ExtractAsDecals { get; set; }
        public bool SaveTextureOrderFile { get; set; }

        public ImageFormat OutputFormat { get; set; }
        public bool SaveAsIndexed { get; set; }
    }


    public static class TextureExtracting
    {
        private const int FirstFullbrightPaletteIndex = 224;


        public static void ExtractTextures(string inputFilePath, string outputDirectory, ExtractionSettings settings, Logger logger)
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

            Util.CreateDirectory(outputDirectory);

            foreach (var texture in textures)
            {
                var isFullbrightTexture = !settings.ExtractAsDecals && TextureName.IsFullbright(texture.Name);

                var maxMipmap = (texture.Type == TextureType.MipmapTexture && settings.ExtractMipmaps) ? 4 : 1;
                for (int mipmap = 0; mipmap < maxMipmap; mipmap++)
                {
                    try
                    {
                        var baseFilePath = Path.Combine(outputDirectory, texture.Name + "." + ImageFileIO.GetDefaultExtension(settings.OutputFormat));

                        var fileSettings = GetOutputFileTextureSettings(texture, mipmap);
                        var filePath = WadMakingSettings.InsertTextureSettingsIntoFilename(baseFilePath, fileSettings);

                        if (texture.Type == TextureType.Font)
                            SaveFontData(texture, baseFilePath, settings.OverwriteExistingFiles, logger);

                        if (!settings.OverwriteExistingFiles && File.Exists(filePath))
                        {
                            logger.Log($"- WARNING: '{filePath}' already exists. Skipping texture.");
                            continue;
                        }

                        if (settings.SaveAsIndexed)
                        {
                            var textureData = texture.GetImageData(mipmap);
                            if (textureData is not null)
                            {
                                var indexedImage = new IndexedImage(textureData, texture.Width >> mipmap, texture.Height >> mipmap, texture.Palette);
                                ImageFileIO.SaveIndexedImage(indexedImage, filePath, settings.OutputFormat);
                                imageFilesCreated += 1;
                            }
                        }
                        else
                        {
                            using (var image = settings.ExtractAsDecals ? DecalTextureToImage(texture, mipmap) : TextureToImage(texture, mipmap))
                            {
                                if (image != null)
                                {
                                    ImageFileIO.SaveImage(image, filePath, settings.OutputFormat);
                                    imageFilesCreated += 1;
                                }
                            }

                            // Create fullbright mask images for textures/mipmaps that contain fullbright pixels:
                            if (isFullbrightTexture && !settings.NoFullbrightMasks && texture.GetImageData(mipmap)?.Any(index => index >= FirstFullbrightPaletteIndex) == true)
                            {
                                fileSettings.IsFullbrightMask = true;
                                var fullbrightFilePath = WadMakingSettings.InsertTextureSettingsIntoFilename(baseFilePath, fileSettings);

                                if (!settings.OverwriteExistingFiles && File.Exists(fullbrightFilePath))
                                {
                                    logger.Log($"- WARNING: '{fullbrightFilePath}' already exists. Skipping fullbright mask.");
                                    continue;
                                }

                                using (var image = TextureToFullbrightMaskImage(texture, mipmap))
                                {
                                    if (image != null)
                                    {
                                        ImageFileIO.SaveImage(image, fullbrightFilePath, settings.OutputFormat);
                                        imageFilesCreated += 1;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"- ERROR: failed to extract '{texture.Name}'{(mipmap > 0 ? $" (mipmap {mipmap})" : "")}: {ex.GetType().Name}: '{ex.Message}'.");
                    }
                }
            }

            if (settings.SaveTextureOrderFile)
            {
                TextureOrder.SaveTextureOrder(outputDirectory, textures.Select(texture => texture.Name));
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
            Util.CreateDirectory(Path.GetDirectoryName(outputWadFilePath));
            wad.Save(outputWadFilePath);

            logger.Log($"Extracted {embeddedTextures.Count} textures from '{inputBspFilePath}' to '{outputWadFilePath}', in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        private static void SaveFontData(Texture fontTexture, string baseFilePath, bool overwriteExistingFiles, Logger logger)
        {
            try
            {
                var filePath = Path.ChangeExtension(baseFilePath, ".font.txt");
                if (!overwriteExistingFiles && File.Exists(filePath))
                {
                    logger.Log($"- WARNING: '{filePath}' already exists. Skipping font data.");
                    return;
                }

                FontData.SaveFontData(fontTexture, filePath);
            }
            catch (Exception ex)
            {
                logger.Log($"- WARNING: Failed to write font data for '{fontTexture.Name}': {ex.GetType().Name}: '{ex.Message}'.");
            }
        }

        private static Image<Rgba32>? DecalTextureToImage(Texture texture, int mipmap = 0)
        {
            var imageData = texture.GetImageData(mipmap);
            if (imageData == null)
                return null;

            var width = texture.Width >> mipmap;
            var height = texture.Height >> mipmap;
            var decalColor = texture.Palette[Constants.DecalColorIndex];

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

        private static Image<Rgba32>? TextureToImage(Texture texture, int mipmap = 0)
        {
            var imageData = texture.GetImageData(mipmap);
            if (imageData == null)
                return null;

            var width = texture.Width >> mipmap;
            var height = texture.Height >> mipmap;
            var hasColorKey = TextureName.IsTransparent(texture.Name) || texture.Type == TextureType.SimpleTexture;

            var image = new Image<Rgba32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var paletteIndex = imageData[y * width + x];
                        if (paletteIndex == Constants.TransparentColorIndex && hasColorKey)
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

        private static Image<Rgba32>? TextureToFullbrightMaskImage(Texture texture, int mipmap = 0)
        {
            var imageData = texture.GetImageData(mipmap);
            if (imageData == null)
                return null;

            var width = texture.Width >> mipmap;
            var height = texture.Height >> mipmap;

            var image = new Image<Rgba32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var paletteIndex = imageData[y * width + x];
                        if (paletteIndex < FirstFullbrightPaletteIndex)
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

        private static TextureSettings GetOutputFileTextureSettings(Texture texture, int mipmap)
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
    }
}
