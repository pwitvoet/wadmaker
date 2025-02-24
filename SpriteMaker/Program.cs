using Shared;
using Shared.FileFormats;
using SixLabors.ImageSharp;
using SpriteMaker.Settings;
using System.Reflection;
using System.Text;

namespace SpriteMaker
{
    class ProgramSettings
    {
        // General settings:
        public bool IncludeSubDirectories { get; set; }         // -subdirs         also processes files in sub-directories (applies to both sprite building and extracting)

        // Build settings:
        public bool FullRebuild { get; set; }                   // -full            forces a full rebuild instead of an incremental one
        public bool EnableSubDirectoryRemoval { get; set; }     // -subdirremoval   enables deleting of output sub-directories when input sub-directories are removed

        // Extract settings:
        public bool Extract { get; set; }                       // -extract         extracts all sprites in the input directory (this is also enabled if the input file is a .spr file)
        public bool ExtractAsSpriteSheet { get; set; }          // -spritesheet     extracts multi-frame sprites as spritesheets instead of image sequences
        public bool OverwriteExistingFiles { get; set; }        // -overwrite       extract mode only, enables overwriting of existing image files (off by default)
        public ImageFormat OutputImageFormat { get; set; }      // -format          Extracted images output format (png, jpg, gif, bmp or tga).
        public bool ExtractAsIndexed { get; set; }              // -indexed         Extracted images are indexed and contain the original texture's palette. Only works with png, gif and bmp.

        // Other settings:
        public string InputPath { get; set; } = "";             // An image or sprite file, or a directory full of images (or sprites, if -extract is set)
        public string OutputPath { get; set; } = "";            // Output sprite or image path, or output directory path

        public bool DisableFileLogging { get; set; }            // -nologfile       disables logging to a file (parent-directory\spritemaker.log)
    }

    class Program
    {
        static TextWriter? LogFile;


        static void Main(string[] args)
        {
            try
            {
                var assemblyName = Assembly.GetExecutingAssembly().GetName();
                var launchInfo = $"{assemblyName.Name}.exe (v{assemblyName.Version}) {string.Join(" ", args)}";
                Log(launchInfo);

                var settings = ParseArguments(args);
                if (!settings.DisableFileLogging)
                {
                    var logName = Path.GetFileNameWithoutExtension(settings.InputPath);
                    var logFilePath = Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? "", $"spritemaker - {logName}.log");
                    LogFile = new StreamWriter(logFilePath, false, Encoding.UTF8);
                    LogFile.WriteLine(launchInfo);
                }

                var logger = new Logger(Log);
                if (settings.Extract)
                {
                    var extractionFormat = settings.ExtractAsSpriteSheet ? ExtractionFormat.Spritesheet :
                           settings.OutputImageFormat == ImageFormat.Gif ? ExtractionFormat.AnimatedGif :
                                                                           ExtractionFormat.ImageSequence;

                    var extractionSettings = new ExtractionSettings {
                        OverwriteExistingFiles = settings.OverwriteExistingFiles,
                        IncludeSubDirectories = settings.IncludeSubDirectories,
                        ExtractionFormat = extractionFormat,
                        OutputFormat = settings.OutputImageFormat,
                        SaveAsIndexed = settings.ExtractAsIndexed,
                    };

                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                    {
                        SpriteExtracting.ExtractSingleSprite(settings.InputPath, settings.OutputPath, extractionSettings, logger);
                    }
                    else
                    {
                        SpriteExtracting.ExtractSprites(settings.InputPath, settings.OutputPath, extractionSettings, logger);
                    }
                }
                else
                {
                    if (File.Exists(settings.InputPath))
                        SpriteMaking.MakeSingleSprite(settings.InputPath, settings.OutputPath, logger);
                    else if (Directory.Exists(settings.InputPath))
                        SpriteMaking.MakeSprites(settings.InputPath, settings.OutputPath, settings.FullRebuild, settings.IncludeSubDirectories, settings.EnableSubDirectoryRemoval, logger);
                    else
                        Log($"ERROR: Can't make sprites, input '{settings.InputPath}' does not exist!");
                }
            }
            catch (InvalidUsageException ex)
            {
                Log($"ERROR: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.GetType().Name}: '{ex.Message}'.");
                Log(ex.StackTrace);
            }
            finally
            {
                LogFile?.Dispose();
            }
        }

        private static ProgramSettings ParseArguments(string[] args)
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
                    case "-full": settings.FullRebuild = true; break;
                    case "-subdirremoval": settings.EnableSubDirectoryRemoval = true; break;
                    case "-extract": settings.Extract = true; break;
                    case "-spritesheet": settings.ExtractAsSpriteSheet = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;

                    case "-format":
                        if (index >= args.Length)
                            throw new InvalidUsageException("The -format parameter must be set to either png, jpg, gif, bmp or tga.");

                        settings.OutputImageFormat = ParseOutputImageFormat(args[index++]);
                        break;

                    case "-indexed": settings.ExtractAsIndexed = true; break;
                    case "-nologfile": settings.DisableFileLogging = true; break;

                    default: throw new InvalidUsageException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new InvalidUsageException("Missing input path (image or sprite file, or folder) argument.");

            if (File.Exists(paths[0]) && Path.GetExtension(paths[0]).ToLowerInvariant() == ".spr")
                settings.Extract = true;


            if (settings.Extract)
            {
                // Sprite extraction requires a spr file path or a directory:
                settings.InputPath = args[index++];

                if (index < args.Length)
                {
                    settings.OutputPath = args[index++];

                    if (Path.GetExtension(settings.OutputPath).ToLowerInvariant() == ".gif")
                        settings.OutputImageFormat = ImageFormat.Gif;
                }
                else
                {
                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                    {
                        // By default, put the output image(s) in the same directory:
                        settings.OutputPath = Path.ChangeExtension(settings.InputPath, "." + ImageFileIO.GetDefaultExtension(settings.OutputImageFormat));
                    }
                    else
                    {
                        // By default, put the output images in a '*_extracted' directory next to the input directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? "", Path.GetFileNameWithoutExtension(settings.InputPath) + "_extracted");
                    }
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
                    var inputIsFile = File.Exists(settings.InputPath);
                    if (inputIsFile)
                    {
                        // By default, put the output sprite in the same directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath)!, SpriteMakingSettings.GetSpriteName(settings.InputPath) + ".spr");
                    }
                    else
                    {
                        // By default, put output sprites in a '*_sprites' directory next to the input directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? "", Path.GetFileNameWithoutExtension(settings.InputPath) + "_sprites");
                    }
                }
            }

            return settings;
        }

        private static ImageFormat ParseOutputImageFormat(string str)
        {
            switch (str.ToLowerInvariant())
            {
                case "png": return ImageFormat.Png;
                case "jpg": return ImageFormat.Jpg;
                case "gif": return ImageFormat.Gif;
                case "bmp": return ImageFormat.Bmp;
                case "tga": return ImageFormat.Tga;

                default: throw new InvalidDataException($"Unknown image format: {str}.");
            }
        }


        private static void Log(string? message)
        {
            Console.WriteLine(message);
            LogFile?.WriteLine(message);
        }
    }
}
