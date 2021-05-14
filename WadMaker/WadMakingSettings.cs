using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WadMaker
{
    /// <summary>
    /// <para>
    /// A collection of texture settings rules, coming from a 'wadmaker.config' file.
    /// 
    /// Rules are put on separate lines, starting with a filename (which can include wildcards: *) and followed by one or more texture settings.
    /// Empty lines and lines starting with // are ignored. Only one rule is applied per texture, with priority given to more specific rules.
    /// </para>
    /// </summary>
    class WadMakingSettings
    {
        const string DitheringAlgorithmKey = "dithering";
        const string DitherScaleKey = "dither-scale";
        const string TransparencyThresholdKey = "transparency-threshold";
        const string WaterFogColorKey = "water-fog";
        const string ConverterKey = "converter";
        const string ConverterArgumentsKey = "arguments";
        const string TimestampKey = "timestamp";
        const string RemovedKey = "removed";

        const string ConfigFilename = "wadmaker.config";
        const string TimestampFilename = "wadmaker.dat";


        class Rule
        {
            public string NamePattern { get; }
            public TextureSettings? TextureSettings { get; }
            public DateTimeOffset LastModified { get; }

            public Rule(string namePattern, TextureSettings? textureSettings, DateTimeOffset lastModified)
            {
                NamePattern = namePattern;
                TextureSettings = textureSettings;
                LastModified = lastModified;
            }
        }


        private Dictionary<string, Rule> _exactRules = new Dictionary<string, Rule>();
        private List<(Regex, Rule)> _wildcardRules = new List<(Regex, Rule)>();

        /// <summary>
        /// Returns texture settings for the given filename, and the time when those settings were last modified.
        /// More specific name patterns (no wildcards) take priority over less specific ones (wildcards).
        /// </summary>
        public (TextureSettings, DateTimeOffset) GetTextureSettings(string filename)
        {
            filename = filename.ToLowerInvariant();

            // Rules without wildcards are the most specific, so they get priority:
            var timestamp = DateTimeOffset.UnixEpoch;
            if (_exactRules.TryGetValue(filename, out var rule))
            {
                if (rule.TextureSettings != null)
                    return (rule.TextureSettings.Value, rule.LastModified);
                else if (rule.LastModified > timestamp)
                    timestamp = rule.LastModified;
            }

            // Wildcard rules are sorted based on the number of non-wildcard characters (with the catch-all '*' pattern coming last):
            foreach ((var regex, var wildcardRule) in _wildcardRules)
            {
                if (regex.IsMatch(filename))
                {
                    if (wildcardRule.TextureSettings != null)
                        return (wildcardRule.TextureSettings.Value, wildcardRule.LastModified);
                    else if (wildcardRule.LastModified > timestamp)
                        timestamp = wildcardRule.LastModified;
                }
            }

            // No specific settings for this file. If there used to be rules that applied to this file,
            // then the timestamp will tell us when the last such rule was removed:
            return (new TextureSettings(), timestamp);
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
                .OrderByDescending(regexRule => regexRule.Item2.NamePattern.Count(c => c != '*'))
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
            var configFilePath = Path.Combine(folder, ConfigFilename);
            var timestampFilePath = Path.Combine(folder, TimestampFilename);

            // First read the timestamps file, which stores the last known state and modification time of each rule:
            var oldRules = new Dictionary<string, Rule>();
            if (File.Exists(timestampFilePath))
            {
                foreach (var line in File.ReadAllLines(timestampFilePath))
                {
                    var rule = ParseRuleLine(line, DateTimeOffset.UnixEpoch, true);
                    if (rule != null)
                        oldRules[rule.NamePattern] = rule;
                }
            }

            // Then read the current rules (wadmaker.config):
            var newRules = new Dictionary<string, Rule>();
            var newTimestamp = DateTimeOffset.UtcNow;
            if (File.Exists(configFilePath))
            {
                newTimestamp = new DateTimeOffset(new FileInfo(configFilePath).LastWriteTimeUtc);
                foreach (var line in File.ReadAllLines(configFilePath))
                {
                    var rule = ParseRuleLine(line, newTimestamp);
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
                    oldRules[namePattern] = newRule;    // Modified settings, so remember the new settings and timestamp
                else
                    newRules[namePattern] = oldRule;    // Same settings, so use the old timestamp (so the texture may not need to be rebuilt).
            }

            foreach (var namePattern in addedNamePatterns)
                oldRules[namePattern] = newRules[namePattern];  // Remember new rules

            foreach (var namePattern in removedNamePatterns)
            {
                // Ignore removal timestamps:
                if (oldRules[namePattern].TextureSettings == null)
                    continue;

                var removedRule = new Rule(namePattern, null, newTimestamp);
                oldRules[namePattern] = removedRule;    // Do not remember removed settings, but do remember when they were removed
                newRules[namePattern] = removedRule;
            }

            // Now save this back to wadmaker.config:
            SaveTimestampedRules(timestampFilePath, oldRules);

            // Finally, return the new rules, which are now properly timestamped:
            return new WadMakingSettings(newRules.Values);
        }

        public static bool IsConfigurationFile(string path)
        {
            var filename = Path.GetFileName(path);
            return filename == ConfigFilename || filename == TimestampFilename;
        }


        private static Rule ParseRuleLine(string line, DateTimeOffset fileTimestamp, bool internalFormat = false)
        {
            var tokens = GetTokens(line).ToArray();
            if (tokens.Length == 0 || tokens[0] == "//")
                return null;

            var i = 0;
            var namePattern = Path.GetFileName(tokens[i++]).ToLowerInvariant();
            var textureSettings = new TextureSettings();
            var isRemoved = false;
            DateTimeOffset? ruleTimestamp = null;
            while (i < tokens.Length)
            {
                var token = tokens[i++];
                if (token == "//")
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

                        default:
                            isHandled = false;
                            break;
                    }

                    if (isHandled)
                        continue;
                }

                switch (token.ToLowerInvariant())
                {
                    case DitheringAlgorithmKey:
                        RequireToken(":");
                        textureSettings.DitheringAlgorithm = ParseToken(ParseDitheringAlgorithm, "dithering algorithm");
                        break;

                    case DitherScaleKey:
                        RequireToken(":");
                        textureSettings.DitherScale = ParseToken(long.Parse, "dither scale");
                        break;

                    case TransparencyThresholdKey:
                        RequireToken(":");
                        textureSettings.TransparencyThreshold = ParseToken(byte.Parse, "transparency threshold");
                        break;

                    case WaterFogColorKey:
                        RequireToken(":");
                        textureSettings.WaterFogColor = new Rgba32(ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse), ParseToken(byte.Parse));
                        break;

                    case ConverterKey:
                        RequireToken(":");
                        textureSettings.Converter = ParseToken(s => s, "converter command string");
                        break;

                    case ConverterArgumentsKey:
                        RequireToken(":");
                        textureSettings.ConverterArguments = ParseToken(s => s, "converter arguments string");
                        if (!textureSettings.ConverterArguments.Contains("{input}") || !textureSettings.ConverterArguments.Contains("{output}"))
                            throw new InvalidDataException("Converter arguments must contain {input} and {output} placeholders.");
                        break;

                    default:
                        throw new InvalidDataException($"Unknown setting: '{token}'.");
                }
            }
            return new Rule(namePattern, isRemoved ? null : (TextureSettings?)textureSettings, ruleTimestamp ?? fileTimestamp);


            void RequireToken(string value)
            {
                if (i >= tokens.Length) throw new InvalidDataException($"Expected a '{value}', but found end of line.");
                if (tokens[i++] != value) throw new InvalidDataException($"Expected a '{value}', but found '{tokens[i - 1]}'.");
            }

            T ParseToken<T>(Func<string, T> parse, string label = null)
            {
                if (i >= tokens.Length)
                    throw new InvalidDataException($"Expected a {label ?? typeof(T).ToString()}, but found end of line.");

                try
                {
                    return parse(tokens[i++]);
                }
                catch (Exception ex)
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
                    yield return "//";
                    start = i + 1;
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

        private static DitheringAlgorithm ParseDitheringAlgorithm(string str)
        {
            switch (str.ToLowerInvariant())
            {
                case "none": return DitheringAlgorithm.None;
                case "floyd-steinberg": return DitheringAlgorithm.FloydSteinberg;
                default: throw new InvalidDataException($"Invalid dithering algorithm: '{str}'.");
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

                        if (settings.DitheringAlgorithm != null) writer.Write($" {DitheringAlgorithmKey}: {Serialize(settings.DitheringAlgorithm.Value)}");
                        if (settings.DitherScale != null) writer.Write($" {DitherScaleKey}: {settings.DitherScale}");
                        if (settings.TransparencyThreshold != null) writer.Write($" {TransparencyThresholdKey}: {settings.TransparencyThreshold}");
                        if (settings.WaterFogColor != null) writer.Write($" {WaterFogColorKey}: {Serialize(settings.WaterFogColor.Value)}");
                        if (settings.Converter != null) writer.Write($" {ConverterKey}: '{settings.Converter}'");
                        if (settings.ConverterArguments != null) writer.Write($" {ConverterArgumentsKey}: '{settings.ConverterArguments}'");
                    }
                    else
                    {
                        writer.Write($" {RemovedKey}");
                    }
                    writer.WriteLine($" {TimestampKey}: {rule.LastModified.ToUnixTimeMilliseconds()}");
                }
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

        private static string Serialize(Rgba32 color) => $"{color.R} {color.G} {color.B} {color.A}";
    }
}
