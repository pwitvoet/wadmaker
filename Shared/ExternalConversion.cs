using System.Diagnostics;

namespace Shared
{
    public static class ExternalConversion
    {
        /// <summary>
        /// Calls an external conversion command. Replaces any occurrence of the special '{input}' and '{output}' markers in <paramref name="converterArguments"/>
        /// with the given <paramref name="inputPath"/> and <paramref name="outputPath"/> respectively. Returns the paths of the resulting file(s).
        /// <para>
        /// Use '{input_escaped}' and '{output_escaped}' to get the input and output paths with escaped backslashes (e.g. 'C:\\directory\\filename' instead of 'C:\directory\filename').
        /// </para>
        /// </summary>
        public static string[] ExecuteConversionCommand(string converter, string converterArguments, string inputPath, string outputPath, Logger logger)
        {
            var arguments = converterArguments
                .Replace("{input}", inputPath)
                .Replace("{output}", outputPath)
                .Replace("{input_escaped}", inputPath.Replace("\\", "\\\\"))
                .Replace("{output_escaped}", outputPath.Replace("\\", "\\\\"))
                .Trim();

            logger.Log($"Executing conversion command: '{converter} {arguments}'.");

            var startInfo = new ProcessStartInfo(converter, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, e) => logger.Log($"    INFO: {e.Data}");
                process.ErrorDataReceived += (sender, e) => logger.Log($"    ERROR: {e.Data}");
                process.Start();
                if (!process.WaitForExit(10_000))
                    throw new TimeoutException("Conversion command did not finish within 10 seconds, .");

                logger.Log($"Conversion command finished with exit code: {process.ExitCode}.");
            }

            return Directory.EnumerateFiles(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath) + "*")
                .ToArray();
        }

        /// <summary>
        /// Throws an exception if the given converter arguments string does not contain an input and output placeholder.
        /// </summary>
        public static void ThrowIfArgumentsAreInvalid(string converterArguments)
        {
            if ((!converterArguments.Contains("{input}") && !converterArguments.Contains("{input_escaped}")) ||
                (!converterArguments.Contains("{output}") && !converterArguments.Contains("{output_escaped")))
                throw new InvalidDataException("Converter arguments must contain {input} (or {input_escaped}) and {output} (or {output_escaped}) placeholders.");
        }

        /// <summary>
        /// Returns a random directory path that can be used as a conversion output directory.
        /// </summary>
        public static string GetConversionOutputDirectory(string parentDirectory) => Path.Combine(parentDirectory, $"converted_{Guid.NewGuid()}");

        /// <summary>
        /// Checks if the given directory name matches the conversion output directory naming convention.
        /// </summary>
        public static bool IsConversionOutputDirectory(string path)
        {
            var directoryName = Path.GetFileName(path);
            return directoryName.StartsWith("converted_") && Guid.TryParse(directoryName.Replace("converted_", ""), out _);
        }
    }
}
