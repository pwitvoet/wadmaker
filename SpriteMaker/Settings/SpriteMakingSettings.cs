using Shared;
using Shared.FileSystem;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;
using System.Text.RegularExpressions;

namespace SpriteMaker.Settings
{
    /// <summary>
    /// A collection of sprite settings rules, coming from a 'spritemaker.config' file.
    /// <para>
    /// Rules are put on separate lines, starting with a filename (which can include wildcards: *) and followed by one or more sprite settings.
    /// Empty lines and lines starting with // are ignored. When multiple rules match a filename, settings defined in more specific rules will take priority.
    /// </para>
    /// </summary>
    class SpriteMakingSettings
    {
        const string ConfigFilename = "spritemaker.config";


        class Rule
        {
            public string NamePattern { get; }
            public SpriteSettings? SpriteSettings { get; }

            public Rule(string namePattern, SpriteSettings? spriteSettings)
            {
                NamePattern = namePattern;
                SpriteSettings = spriteSettings;
            }
        }


        private Dictionary<string, Rule> _exactRules = new Dictionary<string, Rule>();
        private List<(Regex, Rule)> _wildcardRules = new List<(Regex, Rule)>();


        /// <summary>
        /// Returns information about the given file: the file hash, sprite settings and the time when the file or its settings were last modified.
        /// Sprite settings can come from multiple config file entries, with more specific name patterns (without wildcards) taking priority over less specific ones (with wildcards).
        /// </summary>
        public SpriteSourceFileInfo GetSpriteSourceFileInfo(string path)
        {
            var fileHash = FileHash.FromFile(path, out var fileSize);

            var spriteSettings = new SpriteSettings();
            foreach (var rule in GetMatchingRules(Path.GetFileName(path)))
            {
                // More specific rules override settings defined by less specific rules:
                if (rule.SpriteSettings is SpriteSettings ruleSettings)
                    spriteSettings.OverrideWith(ruleSettings);
            }

            // Filename settings take priority over config file settings:
            var filenameSettings = GetSpriteSettingsFromFilename(path);
            spriteSettings.OverrideWith(filenameSettings);

            var lastWriteTime = new System.IO.FileInfo(path).LastWriteTimeUtc;
            return new SpriteSourceFileInfo(path, fileSize, fileHash, lastWriteTime, spriteSettings);
        }

        /// <summary>
        /// Returns the sprite name for the given file path.
        /// This is the first part of the filename, up to the first dot (.).
        /// </summary>
        public static string GetSpriteName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);

            var dotIndex = name.IndexOf('.');
            if (dotIndex >= 0)
                name = name.Substring(0, dotIndex);

            return name.ToLowerInvariant();
        }


        // Returns all rules that match the given filename, from least to most specific.
        private IEnumerable<Rule> GetMatchingRules(string filename)
        {
            filename = filename.ToLowerInvariant();
            var spriteName = GetSpriteName(filename);

            foreach ((var regex, var wildcardRule) in _wildcardRules)
                if (regex.IsMatch(filename))
                    yield return wildcardRule;

            if (_exactRules.TryGetValue(filename, out var rule) || _exactRules.TryGetValue(spriteName, out rule))
                yield return rule;
        }


        private SpriteMakingSettings(IEnumerable<Rule> rules)
        {
            foreach (var rule in rules)
            {
                if (rule.NamePattern.Contains("*"))
                    _wildcardRules.Add((MakeNamePatternRegex(rule.NamePattern), rule));
                else
                    _exactRules[rule.NamePattern] = rule;
            }

            // We'll treat longer patterns (excluding wildcard characters) as more specific, and give them priority:
            _wildcardRules = _wildcardRules
                .OrderBy(regexRule => regexRule.Item2.NamePattern.Count(c => c != '*'))
                .ToList();
        }

        private static Regex MakeNamePatternRegex(string namePattern)
        {
            var regex = Regex.Replace(namePattern, @"\\\*|\*|\\|[^\*\\]*", match =>
            {
                switch (match.Value)
                {
                    case @"*": return ".*";                     // A wildcard can be anything (including empty)
                    case @"\*": return Regex.Escape("*");       // A literal * must be escaped (\*)
                    default: return Regex.Escape(match.Value);  // There are no other special characters
                }
            });
            return new Regex(regex);
        }


        /// <summary>
        /// Reads sprite settings from the spritemaker.config file in the given folder, if it exists.
        /// </summary>
        public static SpriteMakingSettings Load(string folder)
        {
            // First read the global rules (spritemaker.config in SpriteMaker.exe's directory):
            var globalConfigFilePath = Path.Combine(AppContext.BaseDirectory, ConfigFilename);
            var rules = new Dictionary<string, Rule>();
            if (File.Exists(globalConfigFilePath))
            {
                foreach (var line in File.ReadAllLines(globalConfigFilePath))
                {
                    var rule = ParseRuleLine(line);
                    if (rule != null)
                        rules[rule.NamePattern] = rule;
                }
            }

            // Then read the specified directory's current rules (spritemaker.config):
            var configFilePath = Path.Combine(folder, ConfigFilename);
            if (File.Exists(configFilePath))
            {
                foreach (var line in File.ReadAllLines(configFilePath))
                {
                    // TODO: It's probably better to overlay local rules onto global rules, instead of fully replacing global rules!

                    // NOTE: Local rules take precedence over global ones.
                    var rule = ParseRuleLine(line);
                    if (rule != null)
                        rules[rule.NamePattern] = rule;
                }
            }

            return new SpriteMakingSettings(rules.Values);
        }

        public static bool IsConfigurationFile(string path) => Path.GetFileName(path) == ConfigFilename;



        // Filename settings:
        public static SpriteSettings GetSpriteSettingsFromFilename(string filename)
        {
            // NOTE: It's possible to have duplicate or conflicting settings in a filename, such as "test.oriented.parallel.png".
            //       We'll just let later segments override earlier segments.

            var settings = new SpriteSettings();
            foreach (var segment in Path.GetFileNameWithoutExtension(filename)
                .Split('.')
                .Skip(1)
                .Select(segment => segment.Trim().ToLowerInvariant()))
            {
                if (TryParseSpriteType(segment, out var spriteType))
                    settings.SpriteType = spriteType;
                else if (TryParseSpriteTextureFormat(segment, out var textureFormat))
                    settings.SpriteTextureFormat = textureFormat;
                else if (TryParseSpritesheetTileSize(segment, out var spritesheetTileSize))
                    settings.SpritesheetTileSize = spritesheetTileSize;
                else if (int.TryParse(segment, out var frameNumber))
                    settings.FrameNumber = frameNumber;
                else if (TryParseFrameOffset(segment, out var frameOffset))
                    settings.FrameOffset = frameOffset;
            }
            return settings;
        }

        public static string InsertSpriteSettingsIntoFilename(string filename, SpriteSettings settings)
        {
            var extension = Path.GetExtension(filename);
            var sb = new StringBuilder();

            if (settings.SpriteType != null && settings.SpriteType != SpriteType.Parallel)
                sb.Append($".{GetSpriteTypeShorthand(settings.SpriteType.Value)}");
            if (settings.SpriteTextureFormat != null && settings.SpriteTextureFormat != SpriteTextureFormat.Additive)
                sb.Append($".{GetSpriteTextureFormatShorthand(settings.SpriteTextureFormat.Value)}");
            if (settings.SpritesheetTileSize != null)
                sb.Append($".{settings.SpritesheetTileSize.Value.Width}x{settings.SpritesheetTileSize.Value.Height}");
            if (settings.FrameNumber != null)
                sb.Append($".{settings.FrameNumber.Value}");
            if (settings.FrameOffset != null)
                sb.Append($".@{settings.FrameOffset.Value.X},{settings.FrameOffset.Value.Y}");

            return Path.ChangeExtension(filename, sb.ToString() + extension);
        }



        private static bool TryParseSpriteType(string str, out SpriteType type)
        {
            switch (str.ToLowerInvariant())
            {
                case "pu":
                case "parallel-upright":
                    type = SpriteType.ParallelUpright;
                    return true;

                case "u":
                case "upright":
                    type = SpriteType.Upright;
                    return true;

                case "p":
                case "parallel":
                    type = SpriteType.Parallel;
                    return true;

                case "o":
                case "oriented":
                    type = SpriteType.Oriented;
                    return true;

                case "po":
                case "parallel-oriented":
                    type = SpriteType.ParallelOriented;
                    return true;

                default:
                    type = default;
                    return false;
            }
        }

        private static string GetSpriteTypeShorthand(SpriteType type)
        {
            switch (type)
            {
                case SpriteType.ParallelUpright: return "pu";
                case SpriteType.Upright: return "u";
                default:
                case SpriteType.Parallel: return "p";
                case SpriteType.Oriented: return "o";
                case SpriteType.ParallelOriented: return "po";
            }
        }

        private static bool TryParseSpriteTextureFormat(string str, out SpriteTextureFormat textureFormat)
        {
            switch (str.ToLowerInvariant())
            {
                case "n":
                case "normal":
                    textureFormat = SpriteTextureFormat.Normal;
                    return true;

                case "a":
                case "additive":
                    textureFormat = SpriteTextureFormat.Additive;
                    return true;

                case "ia":
                case "index-alpha":
                    textureFormat = SpriteTextureFormat.IndexAlpha;
                    return true;

                case "at":
                case "alpha-test":
                    textureFormat = SpriteTextureFormat.AlphaTest;
                    return true;

                default:
                    textureFormat = default;
                    return false;
            }
        }

        private static string GetSpriteTextureFormatShorthand(SpriteTextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case SpriteTextureFormat.Normal: return "n";
                default:
                case SpriteTextureFormat.Additive: return "a";
                case SpriteTextureFormat.IndexAlpha: return "ia";
                case SpriteTextureFormat.AlphaTest: return "at";
            }
        }

        private static bool TryParseSpritesheetTileSize(string str, out Size size)
        {
            var parts = str.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var width) && int.TryParse(parts[1].Trim(), out var height))
            {
                size = new Size(width, height);
                return true;
            }
            else
            {
                size = default;
                return false;
            }
        }

        private static bool TryParseFrameOffset(string str, out Point point)
        {
            if (str.StartsWith("@"))
            {
                var parts = str.Substring(1).Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var x) && int.TryParse(parts[1].Trim(), out var y))
                {
                    point = new Point(x, y);
                    return true;
                }
            }

            point = default;
            return false;
        }


        #region Parsing/serialization

        const string SpriteTypeKey = "type";
        const string SpriteTextureFormatKey = "texture-format";
        const string FrameOffsetKey = "frame-offset";
        const string PreservePaletteKey = "preserve-palette";
        const string DitheringAlgorithmKey = "dithering";
        const string DitherScaleKey = "dither-scale";
        const string AlphaTestTransparencyThresholdKey = "transparency-threshold";
        const string AlphaTestTransparencyColorKey = "transparency-color";
        const string IndexAlphaTransparencySourceKey = "transparency-input";
        const string IndexAlphaColorKey = "color";
        const string ConverterKey = "converter";
        const string ConverterArgumentsKey = "arguments";
        const string IgnoreKey = "ignore";


        private static Rule? ParseRuleLine(string line)
        {
            var tokens = GetTokens(line).ToArray();
            if (tokens.Length == 0 || IsComment(tokens[0]))
                return null;

            var i = 0;
            var namePattern = Path.GetFileName(tokens[i++]).ToLowerInvariant();
            var spriteSettings = new SpriteSettings();
            while (i < tokens.Length)
            {
                var token = tokens[i++];
                if (IsComment(token))
                    break;

                switch (token.ToLowerInvariant())
                {
                    case SpriteTypeKey:
                        RequireToken(":");
                        spriteSettings.SpriteType = ParseToken(ParseSpriteType, "sprite type");
                        break;

                    case SpriteTextureFormatKey:
                        RequireToken(":");
                        spriteSettings.SpriteTextureFormat = ParseToken(ParseSpriteTextureFormat, "sprite texture format");
                        break;

                    case FrameOffsetKey:
                        RequireToken(":");
                        spriteSettings.FrameOffset = new Point(ParseToken(int.Parse), ParseToken(int.Parse));
                        break;

                    case PreservePaletteKey:
                        RequireToken(":");
                        spriteSettings.PreservePalette = ParseToken(bool.Parse);
                        break;

                    case DitheringAlgorithmKey:
                        RequireToken(":");
                        spriteSettings.DitheringAlgorithm = ParseToken(ParseDitheringAlgorithm, "dithering algorithm");
                        break;

                    case DitherScaleKey:
                        RequireToken(":");
                        spriteSettings.DitherScale = ParseToken(float.Parse, "dither scale");
                        break;

                    case AlphaTestTransparencyThresholdKey:
                        RequireToken(":");
                        spriteSettings.AlphaTestTransparencyThreshold = ParseToken(byte.Parse, "alpha-test transparency threshold");
                        break;

                    case AlphaTestTransparencyColorKey:
                        RequireToken(":");
                        spriteSettings.AlphaTestTransparencyColor = new Rgba32(ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse));
                        break;

                    case IndexAlphaTransparencySourceKey:
                        RequireToken(":");
                        spriteSettings.IndexAlphaTransparencySource = ParseToken(ParseAlphaTestTransparencySource, "index-alpha transparency source");
                        break;

                    case IndexAlphaColorKey:
                        RequireToken(":");
                        spriteSettings.IndexAlphaColor = new Rgba32(ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse));
                        break;

                    case ConverterKey:
                        RequireToken(":");
                        spriteSettings.Converter = ParseToken(s => s, "converter command string");
                        break;

                    case ConverterArgumentsKey:
                        RequireToken(":");
                        spriteSettings.ConverterArguments = ParseToken(s => s, "converter arguments string");
                        ExternalConversion.ThrowIfArgumentsAreInvalid(spriteSettings.ConverterArguments);
                        break;

                    case IgnoreKey:
                        RequireToken(":");
                        spriteSettings.Ignore = ParseToken(bool.Parse);
                        break;

                    default:
                        throw new InvalidDataException($"Unknown setting: '{token}'.");
                }
            }
            return new Rule(namePattern, spriteSettings);


            void RequireToken(string value)
            {
                if (i >= tokens.Length) throw new InvalidDataException($"Expected a '{value}', but found end of line.");
                if (tokens[i++] != value) throw new InvalidDataException($"Expected a '{value}', but found '{tokens[i - 1]}'.");
            }

            T ParseToken<T>(Func<string, T> parse, string? label = null)
            {
                if (i >= tokens.Length)
                    throw new InvalidDataException($"Expected a {label ?? typeof(T).ToString()}, but found end of line.");

                try
                {
                    return parse(tokens[i++]);
                }
                catch (Exception)
                {
                    throw new InvalidDataException($"Expected a {label ?? typeof(T).ToString()}, but found '{tokens[i - 1]}'.");
                }
            }
        }

        private static IEnumerable<string> GetTokens(string line)
        {
            var start = 0;
            var isString = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (isString)
                {
                    if (c == '\'' && line[i - 1] != '\\')
                    {
                        yield return Token(i).Replace(@"\'", "'");
                        start = i + 1;
                        isString = false;
                    }
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (i > start) yield return Token(i);
                    start = i + 1;
                }
                else if (c == ':')
                {
                    if (i > start) yield return Token(i);
                    yield return ":";
                    start = i + 1;
                }
                else if (c == '/' && i > start && line[i - 1] == '/')
                {
                    if (i - 1 > start) yield return Token(i - 1);
                    start = i - 1;
                    yield return Token(line.Length);
                    yield break;
                }
                else if (c == '\'')
                {
                    if (i > start) yield return Token(i);
                    start = i + 1;
                    isString = true;
                }
            }

            if (isString) throw new InvalidDataException($"Expected a ' but found end of line.");
            if (start < line.Length) yield return Token(line.Length);

            string Token(int end) => line.Substring(start, end - start);
        }

        private static bool IsComment(string token) => token?.StartsWith("//") == true;


        private static SpriteType ParseSpriteType(string str)
        {
            if (!TryParseSpriteType(str, out var type))
                throw new InvalidDataException($"Invalid sprite type: '{str}'.");

            return type;
        }

        private static SpriteTextureFormat ParseSpriteTextureFormat(string str)
        {
            if (!TryParseSpriteTextureFormat(str, out var textureFormat))
                throw new InvalidDataException($"Invalid sprite texture format: '{str}'.");

            return textureFormat;
        }

        private static DitheringAlgorithm ParseDitheringAlgorithm(string str)
        {
            switch (str.ToLowerInvariant())
            {
                case "none": return DitheringAlgorithm.None;
                case "floyd-steinberg": return DitheringAlgorithm.FloydSteinberg;
                default: throw new InvalidDataException($"Invalid dithering algorithm: '{str}'.");
            }
        }

        private static IndexAlphaTransparencySource ParseAlphaTestTransparencySource(string str)
        {
            switch (str.ToLowerInvariant())
            {
                case "alpha-channel": return IndexAlphaTransparencySource.AlphaChannel;
                case "grayscale": return IndexAlphaTransparencySource.Grayscale;
                default: throw new InvalidDataException($"Invalid index-alpha transparency source: '{str}'.");
            }
        }

        #endregion
    }
}
