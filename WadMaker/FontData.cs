using Shared;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WadMaker.Settings;

namespace WadMaker
{
    public class FontData
    {
        public int CharacterHeight { get; set; }
        public int RowCount { get; set; }
        public CharInfo[] CharInfos { get; set; }


        private FontData(int characterHeight, int rowCount, CharInfo[] charInfos)
        {
            CharacterHeight = characterHeight;
            RowCount = rowCount;
            CharInfos = charInfos;
        }


        /// <summary>
        /// Reads font data (character rectangles) from a .font.txt file.
        /// Throws an <see cref="InvalidDataException"/> or <see cref="FormatException"/> if the content of the file is not valid.
        /// </summary>
        public static FontData LoadFontData(string filePath, Logger logger)
        {
            int? fontHeight = null;
            int? rowCount = null;
            var charInfos = new CharInfo[Constants.FontCharacterCount];

            var lineNumber = 0;
            var currentX = 0;
            var currentY = 0;
            using (var file = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(file, Encoding.UTF8, leaveOpen: true))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line is null)
                        break;

                    lineNumber += 1;
                    line = RemoveTrailingComments(line).Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (line.StartsWith("char-height:"))
                    {
                        fontHeight = ReadPositiveIntField(line);
                    }
                    else if (line.StartsWith("row-count:"))
                    {
                        rowCount = ReadPositiveIntField(line);
                    }
                    else
                    {
                        (var character, var x, var y, var width) = ReadCharInfoLine(line);
                        if (charInfos[character].CharWidth > 0)
                            logger.Log($"- WARNING: Duplicate entry for character '{GetCharacterLiteral(character)}' at line #{lineNumber}, previous entry will be ignored.");

                        var startOffset = (y ?? currentY) * Constants.FontImageWidth + (x ?? currentX);
                        var charInfo = new CharInfo(startOffset, width);
                        charInfos[character] = charInfo;

                        currentX = (x ?? currentX) + width;
                        currentY = (y ?? currentY);
                    }
                }
            }

            if (fontHeight is null)
                throw new InvalidDataException("Missing 'font-height' value.");
            if (rowCount is null)
                throw new InvalidDataException("Missing 'row-count' value.");

            return new FontData(fontHeight.Value, rowCount.Value, charInfos);


            string RemoveTrailingComments(string line)
            {
                var commentStart = line.IndexOf("//");
                return commentStart < 0 ? line : line.Substring(0, commentStart);
            }

            int ReadPositiveIntField(string line)
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var value) && value >= 0)
                    return value;

                throw new FormatException($"Invalid syntax: '{line}'. Value must be a (positive) number.");
            }

            (char character, int? x, int? y, int width) ReadCharInfoLine(string line)
            {
                // Expected format:
                // <char> <x> <y> <width>
                // or:
                // <char> <width>
                // where <char> is either a single character, \s, \\ or \00 to \ff (the character value as two hexadecimal digits).

                var match = Regex.Match(line, @"(?<char>[ -\[\]-~]|\\s|\\\\|\\[0-9a-fA-F]{2})\s+(?<val1>\d+)(?:\s+(?<val2>\d+)\s+(?<val3>\d+))?");
                if (!match.Success)
                    throw new FormatException($"Invalid character data format at line #{lineNumber}.");

                var character = ParseCharacterLiteral(match.Groups["char"].Value);
                var value1 = int.Parse(match.Groups["val1"].Value);

                if (match.Groups["val2"].Success && match.Groups["val3"].Success)
                {
                    var value2 = int.Parse(match.Groups["val2"].Value);
                    var value3 = int.Parse(match.Groups["val3"].Value);

                    return (character, x: value1, y: value2, width: value3);
                }
                else
                {
                    return (character, x: null, y: null, width: value1);
                }
            }
        }

        /// <summary>
        /// Saves font data (character rectangles) to a .font.txt file.
        /// </summary>
        public static void SaveFontData(Texture fontTexture, string filePath)
        {
            using (var file = File.Create(filePath))
            using (var writer = new StreamWriter(file, Encoding.UTF8, leaveOpen: true))
            {
                writer.WriteLine("// All character rectangles have the same height:");
                writer.WriteLine($"char-height: {fontTexture.CharHeight}");
                writer.WriteLine();
                writer.WriteLine($"row-count: {fontTexture.RowCount}"); // TODO: This doesn't seem useful, and it could be inferred from the character positions anyway?
                writer.WriteLine();
                writer.WriteLine("// Character rectangles. Each line defines a separate character. Lines use the following format:");
                writer.WriteLine("// <char> <x> <y> <width>");
                writer.WriteLine("// - <char> can be a single character (a), or an escape sequence (either \\s for space, \\\\ for \\, or \\xx, the character value in hexadecimal).");
                writer.WriteLine("// - <x> and <y> can be left out. <y> will then be the same as the previous character, and <x> will be the <x> plus the <width> of the previous character.");
                if (fontTexture.CharInfos is not null)
                {
                    var currentX = 0;
                    var currentY = 0;
                    for (int i = 0; i < fontTexture.CharInfos.Length; i++)
                    {
                        var charInfo = fontTexture.CharInfos[i];
                        if (charInfo.CharWidth == 0)
                            continue;

                        var character = GetCharacterLiteral((char)i);
                        var x = charInfo.StartOffset % fontTexture.Width;
                        var y = charInfo.StartOffset / fontTexture.Width;

                        if (x == currentX && y == currentY)
                            writer.WriteLine($"{character} {charInfo.CharWidth}");
                        else
                            writer.WriteLine($"{character} {x} {y} {charInfo.CharWidth}");

                        currentX = x + charInfo.CharWidth;
                        currentY = y;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the .font.txt file path for the given source image file path.
        /// </summary>
        public static string GetFilePath(string sourceImageFilePath)
            => Path.Combine(Path.GetDirectoryName(sourceImageFilePath) ?? "", $"{WadMakingSettings.GetTextureName(sourceImageFilePath)}.font.txt");


        private static char ParseCharacterLiteral(string literal)
        {
            if (literal == @"\s")
                return ' ';
            else if (literal == @"\\")
                return '\\';
            else if (literal.StartsWith('\\'))
                return (char)int.Parse(literal.AsSpan(1), NumberStyles.HexNumber);
            else
                return literal[0];
        }

        private static  string GetCharacterLiteral(char chr)
        {
            if (chr == ' ')
                return @"\s";
            else if (chr == '\\')
                return @"\\";
            else if (chr >= ' ' && chr <= '~')
                return $"{chr}";
            else
                return $"\\{(int)chr:x2}";
        }
    }
}
