using Shared;
using Shared.FileSystem;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;
using System.Text.RegularExpressions;

namespace WadMaker.Settings
{
    /// <summary>
    /// A collection of texture settings rules, coming from a 'wadmaker.config' file.
    /// <para>
    /// Rules are put on separate lines, starting with a filename (which can include wildcards: *) and followed by one or more texture settings.
    /// Empty lines and lines starting with // are ignored. When multiple rules match a filename, settings defined in more specific rules will take priority.
    /// </para>
    /// </summary>
    class WadMakingSettings
    {
        const string ConfigFilename = "wadmaker.config";


        class Rule
        {
            public int Order { get; }
            public string NamePattern { get; }
            public TextureSettings TextureSettings { get; }

            public Rule(int order, string namePattern, TextureSettings textureSettings)
            {
                Order = order;
                NamePattern = namePattern;
                TextureSettings = textureSettings;
            }
        }


        private Dictionary<string, Rule[]> _exactRules = new();
        private List<(Regex, Rule[])> _wildcardRules = new();


        /// <summary>
        /// Returns information about the given file: the file hash, texture settings and the time when the file or its settings were last modified.
        /// Texture settings can come from multiple config file entries, with more specific name patterns (without wildcards) taking priority over less specific ones (with wildcards).
        /// </summary>
        public TextureSourceFileInfo GetTextureSourceFileInfo(string path)
        {
            var fileHash = FileHash.FromFile(path, out var fileSize);

            // Later rules override settings defined by earlier rules:
            var textureSettings = new TextureSettings();
            foreach (var rule in GetMatchingRules(Path.GetFileName(path)))
                textureSettings.OverrideWith(rule.TextureSettings);

            // Filename settings take priority over config file settings:
            var filenameSettings = GetTextureSettingsFromFilename(path);
            textureSettings.OverrideWith(filenameSettings);

            var lastWriteTime = new System.IO.FileInfo(path).LastWriteTimeUtc;
            return new TextureSourceFileInfo(path, fileSize, fileHash, lastWriteTime, textureSettings);
        }

        /// <summary>
        /// Returns the texture name for the given file path.
        /// This is the first part of the filename, up to the first dot (.).
        /// </summary>
        public static string GetTextureName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);

            var dotIndex = name.IndexOf('.');
            if (dotIndex >= 0)
                name = name.Substring(0, dotIndex);

            return name.ToLowerInvariant();
        }


        // Returns all rules that match the given filename, based on order of appearance:
        private IEnumerable<Rule> GetMatchingRules(string filename)
        {
            filename = filename.ToLowerInvariant();
            var textureName = GetTextureName(filename);

            var matchingRules = new List<Rule>();
            foreach ((var regex, var wildcardRules) in _wildcardRules)
            {
                if (regex.IsMatch(filename))
                    matchingRules.AddRange(wildcardRules);
            }

            if (_exactRules.TryGetValue(filename, out var ruleList) || _exactRules.TryGetValue(textureName, out ruleList))
                matchingRules.AddRange(ruleList);

            return matchingRules.OrderBy(rule => rule.Order);
        }


        private WadMakingSettings(IEnumerable<Rule> rules)
        {
            foreach (var group in rules.GroupBy(rule => rule.NamePattern))
            {
                if (group.Key.Contains('*'))
                    _wildcardRules.Add((MakeNamePatternRegex(group.Key), group.ToArray()));
                else
                    _exactRules[group.Key] = group.ToArray();
            }
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
        /// Reads texture settings from the wadmaker.config file in the given folder, if it exists.
        /// </summary>
        public static WadMakingSettings Load(string folder)
        {
            // First read the global rules (wadmaker.config in WadMaker.exe's directory):
            var globalConfigFilePath = Path.Combine(AppContext.BaseDirectory, ConfigFilename);
            var rules = new List<Rule>();
            if (File.Exists(globalConfigFilePath))
            {
                foreach (var line in File.ReadAllLines(globalConfigFilePath))
                    AddRule(ParseRuleLine(line, rules.Count));
            }

            // Then read the specified directory's current rules (wadmaker.config):
            var configFilePath = Path.Combine(folder, ConfigFilename);
            if (File.Exists(configFilePath))
            {
                // NOTE: Local rules take precedence over global ones.
                foreach (var line in File.ReadAllLines(configFilePath))
                    AddRule(ParseRuleLine(line, rules.Count));
            }

            return new WadMakingSettings(rules);


            void AddRule(Rule? rule)
            {
                if (rule is not null)
                    rules.Add(rule);
            }
        }

        public static bool IsConfigurationFile(string path) => Path.GetFileName(path) == ConfigFilename;


        // Filename settings:
        public static TextureSettings GetTextureSettingsFromFilename(string filename)
        {
            // NOTE: It's possible to have duplicate or conflicting settings in a filename, such as "test.mipmap1.mipmap2.png".
            //       We'll just let later segments override earlier segments.

            var settings = new TextureSettings();
            foreach (var segment in Path.GetFileNameWithoutExtension(filename)
                .Split('.')
                .Skip(1)
                .Select(segment => segment.Trim().ToLowerInvariant()))
            {
                if (TryParseTextureType(segment, out var textureType))
                    settings.TextureType = textureType;
                else if (IsFullbrightMaskSelector(segment))
                    settings.IsFullbrightMask = true;
                else if (TryParseMipmapLevel(segment, out var mipmapLevel))
                    settings.MipmapLevel = mipmapLevel;
                else if (TryParseWaterFogColor(segment, out var waterFogColor))
                    settings.WaterFogColor = waterFogColor;
            }
            return settings;
        }

        public static string InsertTextureSettingsIntoFilename(string filename, TextureSettings settings)
        {
            var extension = Path.GetExtension(filename);
            var sb = new StringBuilder();

            if (settings.TextureType != null && settings.TextureType != TextureType.MipmapTexture)
                sb.Append("." + Serialization.ToString(settings.TextureType.Value));
            if (settings.IsFullbrightMask == true)
                sb.Append(".fullbright");
            if (settings.MipmapLevel != null && settings.MipmapLevel != MipmapLevel.Main)
                sb.Append($".mipmap{(int)settings.MipmapLevel}");
            if (settings.WaterFogColor != null)
                sb.Append($".fog {settings.WaterFogColor.Value.R} {settings.WaterFogColor.Value.G} {settings.WaterFogColor.Value.B} {settings.WaterFogColor.Value.A}");

            return Path.ChangeExtension(filename, sb.ToString() + extension);
        }


        private static bool TryParseTextureType(string str, out TextureType textureType)
        {
            switch (str)
            {
                case "qpic": textureType = TextureType.SimpleTexture; return true;
                case "font": textureType = TextureType.Font; return true;
                default: textureType = default; return false;
            }
        }

        private static bool IsFullbrightMaskSelector(string str) => str == "fullbright";

        private static bool TryParseMipmapLevel(string str, out MipmapLevel mipmapLevel)
        {
            switch (str)
            {
                case "mipmap1": mipmapLevel = MipmapLevel.Mipmap1; return true;
                case "mipmap2": mipmapLevel = MipmapLevel.Mipmap2; return true;
                case "mipmap3": mipmapLevel = MipmapLevel.Mipmap3; return true;
                default: mipmapLevel = default; return false;
            }
        }

        private static bool TryParseWaterFogColor(string str, out Rgba32 waterFogColor)
        {
            if (str.StartsWith("fog"))
            {
                var parts = str.Substring(3)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .ToArray();
                if (parts.Length == 4 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b) && byte.TryParse(parts[3], out var a))
                {
                    waterFogColor = new Rgba32(r, g, b, a);
                    return true;
                }
            }

            waterFogColor = default;
            return false;
        }


        #region Parsing/serialization

        const string IgnoreKey = "ignore";
        const string TextureTypeKey = "texture-type";
        const string NoFullbrightKey = "no-fullbright";
        const string FullbrightAlphaThresholdKey = "fullbright-alpha-threshold";
        const string PreservePaletteKey = "preserve-palette";
        const string DitheringAlgorithmKey = "dithering";
        const string DitherScaleKey = "dither-scale";
        const string TransparencyThresholdKey = "transparency-threshold";
        const string TransparencyColorKey = "transparency-color";
        const string WaterFogColorKey = "water-fog";
        const string DecalTransparencyKey = "decal-transparency";
        const string DecalColorKey = "decal-color";
        const string ConverterKey = "converter";
        const string ConverterArgumentsKey = "arguments";


        private static Rule? ParseRuleLine(string line, int order)
        {
            var tokens = GetTokens(line).ToArray();
            if (tokens.Length == 0 || IsComment(tokens[0]))
                return null;

            var i = 0;
            var namePattern = Path.GetFileName(tokens[i++]).ToLowerInvariant();
            var textureSettings = new TextureSettings();
            while (i < tokens.Length)
            {
                var token = tokens[i++];
                if (IsComment(token))
                    break;

                switch (token.ToLowerInvariant())
                {
                    case IgnoreKey:
                        RequireToken(":");
                        textureSettings.Ignore = ParseToken(bool.Parse);
                        break;

                    case TextureTypeKey:
                        RequireToken(":");
                        textureSettings.TextureType = ParseToken(Serialization.ReadTextureType, "texture type");
                        break;

                    case NoFullbrightKey:
                        RequireToken(":");
                        textureSettings.NoFullbright = ParseToken(bool.Parse);
                        break;

                    case FullbrightAlphaThresholdKey:
                        RequireToken(":");
                        textureSettings.FullbrightAlphaThreshold = ParseToken(byte.Parse, "fullbright alpha threshold");
                        break;

                    case PreservePaletteKey:
                        RequireToken(":");
                        textureSettings.PreservePalette = ParseToken(bool.Parse);
                        break;

                    case DitheringAlgorithmKey:
                        RequireToken(":");
                        textureSettings.DitheringAlgorithm = ParseToken(Serialization.ReadDitheringAlgorithm, "dithering algorithm");
                        break;

                    case DitherScaleKey:
                        RequireToken(":");
                        textureSettings.DitherScale = ParseToken(float.Parse, "dither scale");
                        break;

                    case TransparencyThresholdKey:
                        RequireToken(":");
                        textureSettings.TransparencyThreshold = ParseToken(byte.Parse, "transparency threshold");
                        break;

                    case TransparencyColorKey:
                        RequireToken(":");
                        textureSettings.TransparencyColor = new Rgba32(ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse));
                        break;

                    case WaterFogColorKey:
                        RequireToken(":");
                        textureSettings.WaterFogColor = new Rgba32(ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse));
                        break;

                    case DecalTransparencyKey:
                        RequireToken(":");
                        textureSettings.DecalTransparencySource = ParseToken(Serialization.ReadDecalTransparencySource, "decal transparency");
                        break;

                    case DecalColorKey:
                        RequireToken(":");
                        textureSettings.DecalColor = new Rgba32(ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse));
                        break;

                    case ConverterKey:
                        RequireToken(":");
                        textureSettings.Converter = ParseToken(s => s, "converter command string");
                        break;

                    case ConverterArgumentsKey:
                        RequireToken(":");
                        textureSettings.ConverterArguments = ParseToken(s => s, "converter arguments string");
                        ExternalConversion.ThrowIfArgumentsAreInvalid(textureSettings.ConverterArguments);
                        break;

                    default:
                        throw new InvalidDataException($"Unknown setting: '{token}'.");
                }
            }
            return new Rule(order, namePattern, textureSettings);


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

        #endregion
    }
}
