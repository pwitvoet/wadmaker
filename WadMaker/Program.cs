using SixLabors.ImageSharp;
using System.Reflection;
using System.Text;
using Shared;
using System.Diagnostics.CodeAnalysis;
using Shared.FileFormats;

namespace WadMaker
{
    class ProgramSettings
    {
        // Build settings:
        public bool FullRebuild { get; set; }               // -full            Forces a full rebuild instead of an incremental one
        public bool IncludeSubDirectories { get; set; }     // -subdirs         Also include images in sub-directories

        // Extract settings:
        [MemberNotNullWhen(true, nameof(InputFilePath))]
        [MemberNotNullWhen(true, nameof(OutputDirectory))]
        public bool Extract { get; set; }                   //                  Texture extraction mode is enabled when the first argument (path) is a wad or bsp file.
        public bool ExtractMipmaps { get; set; }            // -mipmaps         Also extract mipmaps
        public bool NoFullbrightMasks { get; set; }         // -nofullbright    Do not extract fullbright masks
        public bool OverwriteExistingFiles { get; set; }    // -overwrite       Extract mode only, enables overwriting of existing image files (off by default)
        public ImageFormat OutputImageFormat { get; set; }  // -format          Extracted images output format (png, jpg, gif, bmp or tga).
        public bool ExtractAsIndexed { get; set; }          // -indexed         Extracted images are indexed and contain the original texture's palette. Only works with png, gif and bmp.

        [MemberNotNullWhen(true, nameof(InputFilePath))]
        [MemberNotNullWhen(true, nameof(OutputFilePath))]
        public bool ExtractToWad { get; set; }              //                  This extraction mode is enabled when the first argument is a bsp file path, and the second a wad file path.

        // Bsp settings:
        [MemberNotNullWhen(true, nameof(InputFilePath))]
        [MemberNotNullWhen(true, nameof(OutputFilePath))]
        public bool RemoveEmbeddedTextures { get; set; }    // -remove          Removes embedded textures from the given bsp file

        [MemberNotNullWhen(true, nameof(InputFilePath))]
        [MemberNotNullWhen(true, nameof(ExtraInputFilePath))]
        [MemberNotNullWhen(true, nameof(OutputFilePath))]
        public bool EmbedTextures { get; set; }

        // Other settings:
        public string? InputDirectory { get; set; }         // Build mode only
        public string? InputFilePath { get; set; }          // Wad or bsp path
        public string? ExtraInputFilePath { get; set; }     // Bsp path (when embedding textures)
        public string? OutputDirectory { get; set; }        // Extract mode only
        public string? OutputFilePath { get; set; }         // Output bsp path (when adding or removing embedded textures)

        public bool DisableFileLogging { get; set; }        // -nologfile   disables logging to a file (parent-directory\wadmaker.log)
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
                    var logName = Path.GetFileNameWithoutExtension(settings.InputDirectory ?? settings.InputFilePath);
                    var logFilePath = Path.Combine(Path.GetDirectoryName(settings.InputDirectory ?? settings.InputFilePath) ?? "", $"wadmaker - {logName}.log");
                    LogFile = new StreamWriter(logFilePath, false, Encoding.UTF8);
                    LogFile.WriteLine(launchInfo);
                }

                var logger = new Logger(Log);
                if (settings.Extract)
                {
                    var extractionSettings = new ExtractionSettings {
                        ExtractMipmaps = settings.ExtractMipmaps,
                        NoFullbrightMasks = settings.NoFullbrightMasks,
                        OverwriteExistingFiles = settings.OverwriteExistingFiles,
                        OutputFormat = settings.OutputImageFormat,
                        SaveAsIndexed = settings.ExtractAsIndexed,
                    };
                    TextureExtracting.ExtractTextures(settings.InputFilePath, settings.OutputDirectory, extractionSettings, logger);
                }
                else if (settings.ExtractToWad)
                {
                    TextureExtracting.ExtractEmbeddedTexturesToWad(settings.InputFilePath, settings.OutputFilePath, logger);
                }
                else if (settings.EmbedTextures)
                {
                    TextureEmbedding.EmbedTextures(settings.InputFilePath, settings.ExtraInputFilePath, settings.OutputFilePath, logger);
                }
                else if (settings.RemoveEmbeddedTextures)
                {
                    TextureEmbedding.RemoveEmbeddedTextures(settings.InputFilePath, settings.OutputFilePath, logger);
                }
                else
                {
                    WadMaking.MakeWad(settings.InputDirectory!, settings.OutputFilePath!, settings.FullRebuild, settings.IncludeSubDirectories, logger);
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
                    case "-full": settings.FullRebuild = true; break;
                    case "-subdirs": settings.IncludeSubDirectories = true; break;
                    case "-mipmaps": settings.ExtractMipmaps = true; break;
                    case "-nofullbright": settings.NoFullbrightMasks = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;

                    case "-format":
                        if (index >= args.Length)
                            throw new InvalidUsageException("The -format parameter must be set to either png, jpg, gif, bmp or tga.");

                        settings.OutputImageFormat = ParseOutputImageFormat(args[index++]);
                        break;

                    case "-indexed": settings.ExtractAsIndexed = true; break;
                    case "-remove": settings.RemoveEmbeddedTextures = true; break;
                    case "-nologfile": settings.DisableFileLogging = true; break;

                    default: throw new InvalidUsageException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new InvalidUsageException("Missing input folder (for wad building) or file (for texture extraction) argument.");

            if (File.Exists(paths[0]))
            {
                var extension = Path.GetExtension(paths[0]).ToLowerInvariant();
                if (extension == ".bsp" && !settings.RemoveEmbeddedTextures)
                {
                    if (paths.Length > 1 && Path.GetExtension(paths[1]).ToLowerInvariant() == ".wad")
                        settings.ExtractToWad = true;
                    else
                        settings.Extract = true;
                }
                else if (extension == ".wad")
                {
                    if (paths.Length > 1 && File.Exists(paths[1]) && Path.GetExtension(paths[1]).ToLowerInvariant() == ".bsp")
                        settings.EmbedTextures = true;
                    else
                        settings.Extract = true;
                }
            }


            if (settings.Extract)
            {
                // Texture extraction requires a wad or bsp file path, and optionally an output folder:
                settings.InputFilePath = args[index++];

                if (index < args.Length)
                    settings.OutputDirectory = args[index++];
                else
                    settings.OutputDirectory = Path.Combine(Path.GetDirectoryName(settings.InputFilePath)!, Path.GetFileNameWithoutExtension(settings.InputFilePath) + "_extracted");
            }
            else if (settings.ExtractToWad)
            {
                // Embedded texture extraction to wad file requires a bsp file path and an output wad file path:
                settings.InputFilePath = args[index++];
                settings.OutputFilePath = args[index++];
            }
            else if (settings.EmbedTextures)
            {
                // Embedding textures requires a wad and a bsp file path, and optionally an output bsp file path:
                settings.InputFilePath = args[index++];
                settings.ExtraInputFilePath = args[index++];

                if (index < args.Length)
                    settings.OutputFilePath = args[index++];
                else
                    settings.OutputFilePath = settings.ExtraInputFilePath;
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
                    settings.OutputFilePath = args[index++];
                else
                    settings.OutputFilePath = $"{Path.GetFileName(settings.InputDirectory)}.wad";

                if (!Path.IsPathRooted(settings.OutputFilePath))
                    settings.OutputFilePath = Path.Combine(Path.GetDirectoryName(settings.InputDirectory) ?? "", settings.OutputFilePath);
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
