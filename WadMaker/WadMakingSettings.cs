using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WadMaker.Drawing;

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
        const string QuantizationStrategyThresholdKey = "quantization-strategy-threshold:";
        const string DitheringKey = "dithering:";
        const string TransparentyThresholdKey = "transparency-threshold:";
        const string MaxErrorDiffusionKey = "max-error-diffusion:";
        const string WaterFogColorKey = "water-fog-color:";
        const string WaterFogIntensityKey = "water-fog-intensity:";
        const string TimestampKey = "timestamp:";
        const string RemovedKey = "removed";


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
            var configFilePath = Path.Combine(folder, "wadmaker.config");
            var timestampFilePath = Path.Combine(folder, "wadmaker.dat");

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

        // TODO: Improve parsing - requiring a space between a key and value when keys are already followed by a colon is error-prone!
        private static Rule ParseRuleLine(string line, DateTimeOffset fileTimestamp, bool internalFormat = false)
        {
            // Ignore comment lines:
            if (line.TrimStart().StartsWith("//"))
                return null;

            var tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return null;

            var i = 0;
            var namePattern = Path.GetFileNameWithoutExtension(tokens[i++]).ToLowerInvariant();
            var textureSettings = new TextureSettings();
            var isRemoved = false;
            DateTimeOffset? ruleTimestamp = null;
            while (i < tokens.Length)
            {
                var token = tokens[i++];

                if (internalFormat)
                {
                    var isHandled = true;
                    switch (token.ToLowerInvariant())
                    {
                        case TimestampKey: ruleTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(tokens[i++])); break;
                        case RemovedKey: isRemoved = true; break;
                        default: isHandled = false; break;
                    }

                    if (isHandled)
                        continue;
                }

                switch (token.ToLowerInvariant())
                {
                    case QuantizationStrategyThresholdKey: textureSettings.QuantizationVolumeSelectionThreshold = int.Parse(tokens[i++]); break;
                    case DitheringKey: textureSettings.DitheringAlgorithm = ParseDitheringAlgorithm(tokens[i++]); break;
                    case TransparentyThresholdKey: textureSettings.TransparencyThreshold = int.Parse(tokens[i++]); break;
                    case MaxErrorDiffusionKey: textureSettings.MaxErrorDiffusion = int.Parse(tokens[i++]); break;
                    case WaterFogColorKey: textureSettings.WaterFogColor = new ColorARGB(byte.Parse(tokens[i++]), byte.Parse(tokens[i++]), byte.Parse(tokens[i++])); break;
                    case WaterFogIntensityKey: textureSettings.WaterFogIntensity = int.Parse(tokens[i++]); break;
                    case "//": i = tokens.Length; break;    // Comment, so skip the rest of the line.
                    default:  throw new InvalidDataException($"Invalid setting: '{token}'.");
                }
            }
            return new Rule(namePattern, isRemoved ? null : (TextureSettings?)textureSettings, ruleTimestamp ?? fileTimestamp);
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

        private static string SerializeDitheringAlgorithm(DitheringAlgorithm dithering)
        {
            switch (dithering)
            {
                case DitheringAlgorithm.None: return "none";
                case DitheringAlgorithm.FloydSteinberg: return "floyd-steinberg";
                default: throw new InvalidDataException($"Invalid dithering algorithm: {dithering}.");
            }
        }

        private static string SerializeColor(ColorARGB color) => $"{color.R} {color.G} {color.B}";


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

                        if (settings.QuantizationVolumeSelectionThreshold != null) writer.Write($" {QuantizationStrategyThresholdKey} {settings.QuantizationVolumeSelectionThreshold}");
                        if (settings.DitheringAlgorithm != null) writer.Write($" {DitheringKey} {SerializeDitheringAlgorithm(settings.DitheringAlgorithm.Value)}");
                        if (settings.TransparencyThreshold != null) writer.Write($" {TransparentyThresholdKey} {settings.TransparencyThreshold}");
                        if (settings.MaxErrorDiffusion != null) writer.Write($" {MaxErrorDiffusionKey} {settings.MaxErrorDiffusion}");
                        if (settings.WaterFogColor != null) writer.Write($" {WaterFogColorKey} {SerializeColor(settings.WaterFogColor.Value)}");
                        if (settings.WaterFogIntensity != null) writer.Write($" {WaterFogIntensityKey} {settings.WaterFogIntensity}");
                    }
                    else
                    {
                        writer.Write($" {RemovedKey}");
                    }
                    writer.WriteLine($" {TimestampKey} {rule.LastModified.ToUnixTimeMilliseconds()}");
                }
            }
        }
    }
}
