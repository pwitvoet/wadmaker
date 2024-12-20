﻿using Shared;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.RegularExpressions;

namespace WadMaker
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
        const string HistoryFilename = "wadmaker.dat";


        class Rule
        {
            public string NamePattern { get; }
            public TextureSettings? TextureSettings { get; }
            public DateTimeOffset LastModified { get; }
            public bool IsGlobal { get; }

            public Rule(string namePattern, TextureSettings? textureSettings, DateTimeOffset lastModified, bool isGlobal)
            {
                NamePattern = namePattern;
                TextureSettings = textureSettings;
                LastModified = lastModified;
                IsGlobal = isGlobal;
            }
        }


        private Dictionary<string, Rule> _exactRules = new Dictionary<string, Rule>();
        private List<(Regex, Rule)> _wildcardRules = new List<(Regex, Rule)>();

        /// <summary>
        /// Returns texture settings for the given filename, and the time when those settings were last modified.
        /// More specific name patterns (no wildcards) take priority over less specific ones (wildcards).
        /// </summary>
        public (TextureSettings settings, DateTimeOffset lastUpdate) GetTextureSettings(string filename)
        {
            var textureSettings = new TextureSettings();
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(0);
            foreach (var rule in GetMatchingRules(filename))
            {
                if (rule.TextureSettings is TextureSettings ruleSettings)
                {
                    // More specific rules override settings defined by less specific rules:
                    if (ruleSettings.Ignore != null)                    textureSettings.Ignore = ruleSettings.Ignore;
                    if (ruleSettings.TextureType != null)               textureSettings.TextureType = ruleSettings.TextureType;
                    if (ruleSettings.DitheringAlgorithm != null)        textureSettings.DitheringAlgorithm = ruleSettings.DitheringAlgorithm;
                    if (ruleSettings.DitherScale != null)               textureSettings.DitherScale = ruleSettings.DitherScale;
                    if (ruleSettings.TransparencyThreshold != null)     textureSettings.TransparencyThreshold = ruleSettings.TransparencyThreshold;
                    if (ruleSettings.TransparencyColor != null)         textureSettings.TransparencyColor = ruleSettings.TransparencyColor;
                    if (ruleSettings.WaterFogColor != null)             textureSettings.WaterFogColor = ruleSettings.WaterFogColor;
                    if (ruleSettings.DecalTransparencySource != null)   textureSettings.DecalTransparencySource = ruleSettings.DecalTransparencySource;
                    if (ruleSettings.DecalColor != null)                textureSettings.DecalColor = ruleSettings.DecalColor;
                    if (ruleSettings.Converter != null)                 textureSettings.Converter = ruleSettings.Converter;
                    if (ruleSettings.ConverterArguments != null)        textureSettings.ConverterArguments = ruleSettings.ConverterArguments;
                }

                if (rule.LastModified > timestamp)
                    timestamp = rule.LastModified;
            }
            return (textureSettings, timestamp);
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

        // Returns all rules that match the given filename, from least to most specific.
        private IEnumerable<Rule> GetMatchingRules(string filename)
        {
            filename = filename.ToLowerInvariant();
            var textureName = GetTextureName(filename);

            foreach ((var regex, var wildcardRule) in _wildcardRules)
                if (regex.IsMatch(filename))
                    yield return wildcardRule;

            if (_exactRules.TryGetValue(filename, out var rule) || _exactRules.TryGetValue(textureName, out rule))
                yield return rule;
        }


        private WadMakingSettings(IEnumerable<Rule> rules)
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
        /// Reads texture settings from the wadmaker.config file in the given folder, if it exists.
        /// Also reads and updates wadmaker.dat, for tracking last-modified times for each individual rule,
        /// so only textures that are affected by a modified rule will be rebuilt.
        /// </summary>
        public static WadMakingSettings Load(string folder)
        {
            var globalConfigFilePath = Path.Combine(AppContext.BaseDirectory, ConfigFilename);
            var configFilePath = Path.Combine(folder, ConfigFilename);
            var historyFilePath = Path.Combine(folder, HistoryFilename);

            // First read the history file, which stores the last known state and modification time of each rule:
            var oldRules = new Dictionary<string, Rule>();
            if (File.Exists(historyFilePath))
            {
                foreach (var line in File.ReadAllLines(historyFilePath))
                {
                    var rule = ParseRuleLine(line, DateTimeOffset.FromUnixTimeMilliseconds(0), true);
                    if (rule != null)
                        oldRules[rule.NamePattern] = rule;
                }
            }

            // Then read the global rules (wadmaker.config in WadMaker.exe's directory):
            var newRules = new Dictionary<string, Rule>();
            var newGlobalTimestamp = DateTimeOffset.UtcNow;
            var newLocalTimestamp = DateTimeOffset.UtcNow;

            if (File.Exists(globalConfigFilePath))
            {
                newGlobalTimestamp = new DateTimeOffset(new FileInfo(globalConfigFilePath).LastWriteTimeUtc);
                foreach (var line in File.ReadAllLines(globalConfigFilePath))
                {
                    var rule = ParseRuleLine(line, newLocalTimestamp, isGlobal: true);
                    if (rule != null)
                        newRules[rule.NamePattern] = rule;
                }
            }

            // And read the specified directory's current rules (wadmaker.config):
            if (File.Exists(configFilePath))
            {
                newLocalTimestamp = new DateTimeOffset(new FileInfo(configFilePath).LastWriteTimeUtc);
                foreach (var line in File.ReadAllLines(configFilePath))
                {
                    // NOTE: Local rules take precedence over global ones.
                    var rule = ParseRuleLine(line, newLocalTimestamp);
                    if (rule != null)
                        newRules[rule.NamePattern] = rule;
                }
            }

            // Combine this information to determine when each rule was last modified
            // (without this, a single rule change would trigger a rebuild for all textures that match *any* rule):
            var newNamePatterns = newRules.Keys.ToHashSet();
            var oldNamePatterns = oldRules.Keys.ToHashSet();
            var existingNamePatterns = newNamePatterns.Intersect(oldNamePatterns).ToArray();
            var addedNamePatterns = newNamePatterns.Except(oldNamePatterns).ToArray();
            var removedNamePatterns = oldNamePatterns.Except(newNamePatterns).ToArray();

            foreach (var namePattern in existingNamePatterns)
            {
                var oldRule = oldRules[namePattern];
                var newRule = newRules[namePattern];
                if (!newRule.TextureSettings.Equals(oldRule.TextureSettings))
                {
                    // Modified settings, so remember the new settings and timestamp.
                    if (!oldRule.IsGlobal && newRule.IsGlobal)
                    {
                        // If removing a local rule exposes an (older) global rule, then use the local config file's last-write-time as timestamp instead,
                        // because that's when the effective settings changed:
                        oldRules[namePattern] = new Rule(namePattern, newRule.TextureSettings, newLocalTimestamp, true);
                    }
                    else
                    {
                        oldRules[namePattern] = newRule;
                    }
                }
                else
                {
                    // Same settings, so use the old timestamp (so the texture may not need to be rebuilt):
                    newRules[namePattern] = oldRule;
                }
            }

            foreach (var namePattern in addedNamePatterns)
                oldRules[namePattern] = newRules[namePattern];  // Remember new rules

            foreach (var namePattern in removedNamePatterns)
            {
                var oldRule = oldRules[namePattern];

                // Ignore removal timestamps:
                if (oldRule.TextureSettings == null)
                    continue;

                var removedRule = new Rule(namePattern, null, oldRule.IsGlobal ? newGlobalTimestamp : newLocalTimestamp, oldRule.IsGlobal);
                oldRules[namePattern] = removedRule;    // Do not remember removed settings, but do remember when they were removed
                newRules[namePattern] = removedRule;
            }

            // Now save this back to wadmaker.dat:
            SaveTimestampedRules(historyFilePath, oldRules);

            // Finally, return the new rules, which are now properly timestamped:
            return new WadMakingSettings(newRules.Values);
        }

        public static bool IsConfigurationFile(string path)
        {
            var filename = Path.GetFileName(path);
            return filename == ConfigFilename || filename == HistoryFilename;
        }


        #region Parsing/serialization

        const string IgnoreKey = "ignore";
        const string TextureTypeKey = "texture-type";
        const string DitheringAlgorithmKey = "dithering";
        const string DitherScaleKey = "dither-scale";
        const string TransparencyThresholdKey = "transparency-threshold";
        const string TransparencyColorKey = "transparency-color";
        const string WaterFogColorKey = "water-fog";
        const string DecalTransparencyKey = "decal-transparency";
        const string DecalColorKey = "decal-color";
        const string ConverterKey = "converter";
        const string ConverterArgumentsKey = "arguments";

        const string TimestampKey = "timestamp";
        const string RemovedKey = "removed";
        const string GlobalKey = "global";


        private static Rule? ParseRuleLine(string line, DateTimeOffset fileTimestamp, bool internalFormat = false, bool isGlobal = false)
        {
            var tokens = GetTokens(line).ToArray();
            if (tokens.Length == 0 || IsComment(tokens[0]))
                return null;

            var i = 0;
            var namePattern = Path.GetFileName(tokens[i++]).ToLowerInvariant();
            var textureSettings = new TextureSettings();
            var isRemoved = false;
            DateTimeOffset? ruleTimestamp = null;
            while (i < tokens.Length)
            {
                var token = tokens[i++];
                if (IsComment(token))
                    break;

                if (internalFormat)
                {
                    var isHandled = true;
                    switch (token.ToLowerInvariant())
                    {
                        case TimestampKey:
                            RequireToken(":");
                            ruleTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(ParseToken(long.Parse, "numeric timestamp"));
                            break;

                        case RemovedKey:
                            isRemoved = true;
                            break;

                        case GlobalKey:
                            isGlobal = true;
                            break;

                        default:
                            isHandled = false;
                            break;
                    }

                    if (isHandled)
                        continue;
                }

                switch (token.ToLowerInvariant())
                {
                    case IgnoreKey:
                        RequireToken(":");
                        textureSettings.Ignore = ParseToken(bool.Parse);
                        break;

                    case TextureTypeKey:
                        RequireToken(":");
                        textureSettings.TextureType = ParseToken(ParseTextureType, "texture type");
                        break;

                    case DitheringAlgorithmKey:
                        RequireToken(":");
                        textureSettings.DitheringAlgorithm = ParseToken(ParseDitheringAlgorithm, "dithering algorithm");
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
                        textureSettings.DecalTransparencySource = ParseToken(ParseDecalTransparency, "decal transparency");
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
            return new Rule(namePattern, isRemoved ? null : (TextureSettings?)textureSettings, ruleTimestamp ?? fileTimestamp, isGlobal);


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

        private static TextureType ParseTextureType(string str)
        {
            switch (str.ToLowerInvariant())
            {
                case "qpic": return TextureType.SimpleTexture;
                case "mipmap": return TextureType.MipmapTexture;
                case "font": return TextureType.Font;
                default: throw new InvalidDataException($"Invalid texture type: '{str}'.");
            }
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

        private static DecalTransparencySource ParseDecalTransparency(string str)
        {
            switch (str.ToLowerInvariant())
            {
                case "alpha": return DecalTransparencySource.AlphaChannel;
                case "grayscale": return DecalTransparencySource.Grayscale;
                default: throw new InvalidDataException($"Invalid decal transparency: '{str}'.");
            }
        }


        private static void SaveTimestampedRules(string path, Dictionary<string, Rule> timestampedRules)
        {
            using (var file = File.Create(path))
            using (var writer = new StreamWriter(file))
            {
                writer.WriteLine("// This file is generated by Wadmaker and is used to keep track of when rules are last modified, so only affected textures will be rebuilt.");
                foreach (var rule in timestampedRules.Values)
                {
                    writer.Write(rule.NamePattern);
                    if (rule.TextureSettings != null)
                    {
                        var settings = rule.TextureSettings.Value;

                        if (settings.Ignore != null) writer.Write($" {IgnoreKey}: {settings.Ignore}");
                        if (settings.TextureType != null) writer.Write($" {TextureTypeKey}: {Serialize(settings.TextureType.Value)}");
                        if (settings.DitheringAlgorithm != null) writer.Write($" {DitheringAlgorithmKey}: {Serialize(settings.DitheringAlgorithm.Value)}");
                        if (settings.DitherScale != null) writer.Write($" {DitherScaleKey}: {settings.DitherScale}");
                        if (settings.TransparencyThreshold != null) writer.Write($" {TransparencyThresholdKey}: {settings.TransparencyThreshold}");
                        if (settings.TransparencyColor != null) writer.Write($" {TransparencyColorKey}: {Serialize(settings.TransparencyColor.Value, false)}");
                        if (settings.WaterFogColor != null) writer.Write($" {WaterFogColorKey}: {Serialize(settings.WaterFogColor.Value)}");
                        if (settings.DecalTransparencySource != null) writer.Write($" {DecalTransparencyKey}: {Serialize(settings.DecalTransparencySource.Value)}");
                        if (settings.DecalColor != null) writer.Write($" {DecalColorKey}: {Serialize(settings.DecalColor.Value, false)}");
                        if (settings.Converter != null) writer.Write($" {ConverterKey}: '{settings.Converter}'");
                        if (settings.ConverterArguments != null) writer.Write($" {ConverterArgumentsKey}: '{settings.ConverterArguments}'");

                        if (rule.IsGlobal) writer.Write($" {GlobalKey}");
                    }
                    else
                    {
                        writer.Write($" {RemovedKey}");
                    }
                    writer.WriteLine($" {TimestampKey}: {rule.LastModified.ToUnixTimeMilliseconds()}");
                }
            }
        }

        private static string Serialize(TextureType textureType)
        {
            switch (textureType)
            {
                case TextureType.SimpleTexture: return "qpic";
                default:
                case TextureType.MipmapTexture: return "mipmap";
                case TextureType.Font: return "font";
            }
        }

        private static string Serialize(DitheringAlgorithm ditheringAlgorithm)
        {
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None: return "none";
                case DitheringAlgorithm.FloydSteinberg: return "floyd-steinberg";
            }
        }

        private static string Serialize(DecalTransparencySource decalTransparency)
        {
            switch (decalTransparency)
            {
                default:
                case DecalTransparencySource.AlphaChannel: return "alpha";
                case DecalTransparencySource.Grayscale: return "grayscale";
            }
        }

        private static string Serialize(Rgba32 color, bool includeAplha = true) => $"{color.R} {color.G} {color.B}" + (includeAplha ? $" {color.A}" : "");

        #endregion
    }
}
