﻿using Shared;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;

namespace SpriteMaker
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
        const string HistoryFilename = "spritemaker.dat";


        class Rule
        {
            public string NamePattern { get; }
            public SpriteSettings? SpriteSettings { get; }
            public DateTimeOffset LastModified { get; }
            public bool IsGlobal { get; }

            public Rule(string namePattern, SpriteSettings? spriteSettings, DateTimeOffset lastModified, bool isGlobal)
            {
                NamePattern = namePattern;
                SpriteSettings = spriteSettings;
                LastModified = lastModified;
                IsGlobal = isGlobal;
            }
        }


        public string Directory { get; }
        public IReadOnlyDictionary<string, byte[]?> FileHashesHistory { get; }
        public IReadOnlyCollection<string> SubDirectoryNamesHistory { get; }

        private Dictionary<string, Rule> _exactRules = new Dictionary<string, Rule>();
        private List<(Regex, Rule)> _wildcardRules = new List<(Regex, Rule)>();
        private Dictionary<string, Rule> _rulesHistory;


        /// <summary>
        /// Returns sprite settings for the given filename, and the time when those settings were last modified.
        /// More specific name patterns (no wildcards) take priority over less specific ones (wildcards).
        /// </summary>
        public (SpriteSettings settings, DateTimeOffset lastUpdate) GetSpriteSettings(string filename)
        {
            var spriteSettings = new SpriteSettings();
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(0);
            foreach (var rule in GetMatchingRules(filename))
            {
                if (rule.SpriteSettings is SpriteSettings ruleSettings)
                {
                    // More specific rules override settings defined by less specific rules:
                    if (ruleSettings.SpriteType != null)                        spriteSettings.SpriteType = ruleSettings.SpriteType;
                    if (ruleSettings.SpriteTextureFormat != null)               spriteSettings.SpriteTextureFormat = ruleSettings.SpriteTextureFormat;
                    if (ruleSettings.FrameOffset != null)                       spriteSettings.FrameOffset = ruleSettings.FrameOffset;
                    if (ruleSettings.DitheringAlgorithm != null)                spriteSettings.DitheringAlgorithm = ruleSettings.DitheringAlgorithm;
                    if (ruleSettings.DitherScale != null)                       spriteSettings.DitherScale = ruleSettings.DitherScale;
                    if (ruleSettings.AlphaTestTransparencyThreshold != null)    spriteSettings.AlphaTestTransparencyThreshold = ruleSettings.AlphaTestTransparencyThreshold;
                    if (ruleSettings.AlphaTestTransparencyColor != null)        spriteSettings.AlphaTestTransparencyColor = ruleSettings.AlphaTestTransparencyColor;
                    if (ruleSettings.IndexAlphaTransparencySource != null)      spriteSettings.IndexAlphaTransparencySource = ruleSettings.IndexAlphaTransparencySource;
                    if (ruleSettings.IndexAlphaColor != null)                   spriteSettings.IndexAlphaColor = ruleSettings.IndexAlphaColor;
                    if (ruleSettings.Converter != null)                         spriteSettings.Converter = ruleSettings.Converter;
                    if (ruleSettings.ConverterArguments != null)                spriteSettings.ConverterArguments = ruleSettings.ConverterArguments;
                    if (ruleSettings.Ignore != null)                            spriteSettings.Ignore = ruleSettings.Ignore;
                }

                if (rule.LastModified > timestamp)
                    timestamp = rule.LastModified;
            }
            return (spriteSettings, timestamp);
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

        /// <summary>
        /// Updates the history file (spritemaker.dat), which stores a condensed history of the spritemaker.config settings, previously seen files and content hashes,
        /// and previously seen sub-directories. This enables SpriteMaker to detect settings, filename and sub-directory changes, allowing it to only update sprites
        /// whose input files or settings have been modified, and to only remove files and sub-directories that have previously been created by SpriteMaker.
        /// </summary>
        public void UpdateHistory(IDictionary<string, byte[]?> currentFileHashes, HashSet<string> currentSubDirectoryNames)
        {
            var historyFilePath = Path.Combine(Directory, HistoryFilename);

            var allFileHashes = currentFileHashes.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var filename in FileHashesHistory.Keys)
            {
                if (!allFileHashes.ContainsKey(filename))
                    allFileHashes[filename] = null;
            }

            var allSubDirectoryNames = SubDirectoryNamesHistory.Union(currentSubDirectoryNames).ToHashSet();

            SaveHistoryFile(historyFilePath, _rulesHistory, allFileHashes, allSubDirectoryNames);
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


        private SpriteMakingSettings(
            string directory,
            IEnumerable<Rule> currentRules,
            IDictionary<string, Rule> rulesHistory,
            IDictionary<string, byte[]?> fileHashesHistory,
            HashSet<string> subDirectoryNamesHistory)
        {
            Directory = directory;
            FileHashesHistory = fileHashesHistory.ToDictionary(kv => kv.Key, kv => kv.Value);
            SubDirectoryNamesHistory = subDirectoryNamesHistory.ToHashSet();

            _rulesHistory = rulesHistory.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var rule in currentRules)
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
        /// If <paramref name="updateHistory"/> is true then this also reads and updates spritemaker.dat,
        /// for tracking last-modified times for each individual rule, so only sprites that are affected by a modified rule will be rebuilt.
        /// </summary>
        public static SpriteMakingSettings Load(string directory)
        {
            var globalConfigFilePath = Path.Combine(AppContext.BaseDirectory, ConfigFilename);
            var configFilePath = Path.Combine(directory, ConfigFilename);
            var historyFilePath = Path.Combine(directory, HistoryFilename);

            // First read the history file, which stores the last known state and modification time of each rule,
            // as well as the names and hashes of previously converted files, and the names of previous sub-directories:
            var oldRules = new Dictionary<string, Rule>();
            var previousFileHashes = new Dictionary<string, byte[]?>();
            var previousSubDirectoryNames = new HashSet<string>();
            if (File.Exists(historyFilePath))
                (oldRules, previousFileHashes, previousSubDirectoryNames) = ParseHistoryFile(historyFilePath);

            // Then read the global rules (spritemaker.config in SpriteMaker.exe's directory):
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

            // And read the specified directory's current rules (spritemaker.config):
            if (File.Exists(configFilePath))
            {
                // NOTE: Local rules take precedence over global ones.
                newLocalTimestamp = new DateTimeOffset(new FileInfo(configFilePath).LastWriteTimeUtc);
                foreach (var line in File.ReadAllLines(configFilePath))
                {
                    var rule = ParseRuleLine(line, newLocalTimestamp);
                    if (rule != null)
                        newRules[rule.NamePattern] = rule;
                }
            }

            // Combine this information to determine when each rule was last modified
            // (without this, a single rule change would trigger a rebuild for all sprites that match *any* rule):
            var newNamePatterns = newRules.Keys.ToHashSet();
            var oldNamePatterns = oldRules.Keys.ToHashSet();
            var existingNamePatterns = newNamePatterns.Intersect(oldNamePatterns).ToArray();
            var addedNamePatterns = newNamePatterns.Except(oldNamePatterns).ToArray();
            var removedNamePatterns = oldNamePatterns.Except(newNamePatterns).ToArray();

            foreach (var namePattern in existingNamePatterns)
            {
                var oldRule = oldRules[namePattern];
                var newRule = newRules[namePattern];
                if (!newRule.SpriteSettings.Equals(oldRule.SpriteSettings))
                {
                    // Modified settings, so remember the new settings and timestamp.
                    if (!oldRule.IsGlobal && newRule.IsGlobal)
                    {
                        // If removing a local rule exposes an (older) global rule, then use the local config file's last-write-time as timestamp instead,
                        // because that's when the effective settings changed:
                        oldRules[namePattern] = new Rule(namePattern, newRule.SpriteSettings, newLocalTimestamp, true);
                    }
                    else
                    {
                        oldRules[namePattern] = newRule;
                    }
                }
                else
                {
                    // Same settings, so use the old timestamp (so the sprite may not need to be rebuilt):
                    newRules[namePattern] = oldRule;
                }
            }

            foreach (var namePattern in addedNamePatterns)
                oldRules[namePattern] = newRules[namePattern];  // Remember new rules

            foreach (var namePattern in removedNamePatterns)
            {
                var oldRule = oldRules[namePattern];

                // Ignore removal timestamps:
                if (oldRule.SpriteSettings == null)
                    continue;

                var removedRule = new Rule(namePattern, null, oldRule.IsGlobal ? newGlobalTimestamp : newLocalTimestamp, oldRule.IsGlobal);
                oldRules[namePattern] = removedRule;    // Do not remember removed settings, but do remember when they were removed
                newRules[namePattern] = removedRule;
            }

            // Finally, return the new rules, which are now properly timestamped:
            return new SpriteMakingSettings(directory, newRules.Values, oldRules, previousFileHashes, previousSubDirectoryNames);
        }

        public static bool IsConfigurationFile(string path)
        {
            var filename = Path.GetFileName(path);
            return filename == ConfigFilename || filename == HistoryFilename;
        }


        #region Parsing/serialization

        public static bool TryParseSpriteType(string str, out SpriteType type)
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

        public static string GetSpriteTypeShorthand(SpriteType type)
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

        public static bool TryParseSpriteTextureFormat(string str, out SpriteTextureFormat textureFormat)
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

        public static string GetSpriteTextureFormatShorthand(SpriteTextureFormat textureFormat)
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


        const string SpriteTypeKey = "type";
        const string SpriteTextureFormatKey = "texture-format";
        const string FrameOffsetKey = "frame-offset";
        const string DitheringAlgorithmKey = "dithering";
        const string DitherScaleKey = "dither-scale";
        const string AlphaTestTransparencyThresholdKey = "transparency-threshold";
        const string AlphaTestTransparencyColorKey = "transparency-color";
        const string IndexAlphaTransparencySourceKey = "transparency-input";
        const string IndexAlphaColorKey = "color";
        const string ConverterKey = "converter";
        const string ConverterArgumentsKey = "arguments";
        const string TimestampKey = "timestamp";
        const string RemovedKey = "removed";
        const string GlobalKey = "global";
        const string IgnoreKey = "ignore";


        const string TimestampsSegmentHeader = "RULE TIMESTAMPS:";
        const string FileHashesSegmentHeader = "FILE HASHES:";
        const string SubDirectoriesSegmentHeader = "SUB DIRECTORIES:";

        enum HistoryFileSegment
        {
            None,
            RuleTimestamps,
            FileHashes,
            SubDirectories,
        }

        private static (Dictionary<string, Rule> oldRules, Dictionary<string, byte[]?> oldFileHashes, HashSet<string> oldSubDirectories) ParseHistoryFile(string path)
        {
            var oldRules = new Dictionary<string, Rule>();
            var oldFileHashes = new Dictionary<string, byte[]?>();
            var oldSubDirectories = new HashSet<string>();

            var lines = File.ReadAllLines(path);
            var segment = HistoryFileSegment.None;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith(@"\\"))
                    continue;

                switch (line)
                {
                    case TimestampsSegmentHeader: segment = HistoryFileSegment.RuleTimestamps; break;
                    case FileHashesSegmentHeader: segment = HistoryFileSegment.FileHashes; break;
                    case SubDirectoriesSegmentHeader: segment = HistoryFileSegment.SubDirectories; break;
                    default:
                    {
                        switch (segment)
                        {
                            case HistoryFileSegment.RuleTimestamps:
                                var rule = ParseRuleLine(line, DateTimeOffset.FromUnixTimeMilliseconds(0), internalFormat: true);
                                if (rule != null)
                                    oldRules[rule.NamePattern] = rule;
                                break;

                            case HistoryFileSegment.FileHashes:
                                var parts = line.Split();
                                var filename = HttpUtility.UrlDecode(parts[0]);
                                var hash = (parts.Length < 2) ? null : ParseHex(parts[1]);
                                oldFileHashes[filename] = hash;
                                break;

                            case HistoryFileSegment.SubDirectories:
                                oldSubDirectories.Add(HttpUtility.UrlDecode(line));
                                break;
                        }
                        break;
                    }
                }
            }

            return (oldRules, oldFileHashes, oldSubDirectories);
        }

        private static Rule? ParseRuleLine(string line, DateTimeOffset fileTimestamp, bool internalFormat = false, bool isGlobal = false)
        {
            var tokens = GetTokens(line).ToArray();
            if (tokens.Length == 0 || IsComment(tokens[0]))
                return null;

            var i = 0;
            var namePattern = Path.GetFileName(tokens[i++]).ToLowerInvariant();
            var spriteSettings = new SpriteSettings();
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
            return new Rule(namePattern, isRemoved ? null : spriteSettings, ruleTimestamp ?? fileTimestamp, isGlobal);


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

        private static byte[] ParseHex(string hexString)
        {
            if (hexString.Length % 2 != 0) throw new InvalidDataException("Hex-string must contain an even number of hexadecimal digits.");

            var bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < hexString.Length; i += 2)
                bytes[i / 2] = byte.Parse(hexString.Substring(i, 2), NumberStyles.HexNumber);

            return bytes;
        }


        private static void SaveHistoryFile(string path, Dictionary<string, Rule> oldRules, IDictionary<string, byte[]?> oldFileHashes, HashSet<string> oldSubDirectoryNames)
        {
            using (var file = File.Create(path))
            using (var writer = new StreamWriter(file))
            {
                writer.WriteLine("// This file is generated by SpriteMaker and is used to keep track of when rules, files and sub-directories are last modified, so only affected sprites will be rebuilt.");
                writer.WriteLine(TimestampsSegmentHeader);
                foreach (var rule in oldRules.Values)
                {
                    writer.Write(rule.NamePattern);
                    if (rule.SpriteSettings != null)
                    {
                        var settings = rule.SpriteSettings.Value;

                        if (settings.SpriteType != null) writer.Write($" {SpriteTypeKey}: {Serialize(settings.SpriteType.Value)}");
                        if (settings.SpriteTextureFormat != null) writer.Write($" {SpriteTextureFormatKey}: {Serialize(settings.SpriteTextureFormat.Value)}");
                        if (settings.FrameOffset != null) writer.Write($" {FrameOffsetKey}: {settings.FrameOffset.Value.X} {settings.FrameOffset.Value.Y}");
                        if (settings.DitheringAlgorithm != null) writer.Write($" {DitheringAlgorithmKey}: {Serialize(settings.DitheringAlgorithm.Value)}");
                        if (settings.DitherScale != null) writer.Write($" {DitherScaleKey}: {settings.DitherScale.Value}");
                        if (settings.AlphaTestTransparencyThreshold != null) writer.Write($" {AlphaTestTransparencyThresholdKey}: {settings.AlphaTestTransparencyThreshold.Value}");
                        if (settings.AlphaTestTransparencyColor != null) writer.Write($" {AlphaTestTransparencyColorKey}: {Serialize(settings.AlphaTestTransparencyColor.Value)}");
                        if (settings.IndexAlphaTransparencySource != null) writer.Write($" {IndexAlphaTransparencySourceKey}: {Serialize(settings.IndexAlphaTransparencySource.Value)}");
                        if (settings.IndexAlphaColor != null) writer.Write($" {IndexAlphaColorKey}: {Serialize(settings.IndexAlphaColor.Value)}");
                        if (settings.Converter != null) writer.Write($" {ConverterKey}: '{settings.Converter}'");
                        if (settings.ConverterArguments != null) writer.Write($" {ConverterArgumentsKey}: '{settings.ConverterArguments}'");
                        if (settings.Ignore != null) writer.Write($" {IgnoreKey}: {settings.Ignore}");

                        if (rule.IsGlobal) writer.Write($" {GlobalKey}");
                    }
                    else
                    {
                        writer.Write($" {RemovedKey}");
                    }
                    writer.WriteLine($" {TimestampKey}: {rule.LastModified.ToUnixTimeMilliseconds()}");
                }

                writer.WriteLine(FileHashesSegmentHeader);
                foreach (var filenameAndHash in oldFileHashes)
                    writer.WriteLine(HttpUtility.UrlEncode(filenameAndHash.Key) + ((filenameAndHash.Value == null) ? "" : " " + string.Join("", filenameAndHash.Value.Select(b => b.ToString("x2")))));

                writer.WriteLine(SubDirectoriesSegmentHeader);
                foreach (var subDirectoryName in oldSubDirectoryNames)
                    writer.WriteLine(HttpUtility.UrlEncode(subDirectoryName));
            }
        }

        private static string Serialize(SpriteType type)
        {
            switch (type)
            {
                case SpriteType.ParallelUpright: return "parallel-upright";
                case SpriteType.Upright: return "upright";
                default:
                case SpriteType.Parallel: return "parallel";
                case SpriteType.Oriented: return "oriented";
                case SpriteType.ParallelOriented: return "parallel-oriented";
            }
        }

        private static string Serialize(SpriteTextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case SpriteTextureFormat.Normal: return "normal";
                default:
                case SpriteTextureFormat.Additive: return "additive";
                case SpriteTextureFormat.IndexAlpha: return "index-alpha";
                case SpriteTextureFormat.AlphaTest: return "alpha-test";
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

        private static string Serialize(IndexAlphaTransparencySource transparencySource)
        {
            switch (transparencySource)
            {
                default:
                case IndexAlphaTransparencySource.AlphaChannel: return "alpha";
                case IndexAlphaTransparencySource.Grayscale: return "grayscale";
            }
        }

        private static string Serialize(Rgba32 color, bool includeAplha = true) => $"{color.R} {color.G} {color.B}" + (includeAplha ? $" {color.A}" : "");

        #endregion
    }
}
