using Shared.FileSystem;
using Shared.JSON;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileInfo = Shared.FileSystem.FileInfo;

namespace SpriteMaker.Settings
{
    class SpriteMakingHistoryJsonSerializer : JsonConverter<SpriteMakingHistory>
    {
        public override void Write(Utf8JsonWriter writer, SpriteMakingHistory value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("sprites");
            writer.WriteStartArray();
            foreach (var sprite in value.Sprites.Values)
                WriteSpriteHistory(writer, sprite);
            writer.WriteEndArray();

            writer.WritePropertyName("sub-directory-names");
            writer.WriteStartArray();
            foreach (var subDirectory in value.SubDirectoryNames)
                writer.WriteStringValue(subDirectory);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        public override SpriteMakingHistory? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var sprites = new Dictionary<string, SpriteMakingHistory.SpriteHistory>();
            var subDirectoryNames = new List<string>();

            reader.ReadStartObject();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                switch (reader.ReadPropertyName())
                {
                    default: reader.SkipValue(); break;

                    case "sprites":
                    {
                        reader.ReadStartArray();

                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            var spriteHistory = ReadSpriteHistory(ref reader);
                            var spriteName = Path.GetFileNameWithoutExtension(spriteHistory.OutputFile.Path);
                            sprites[spriteName] = spriteHistory;
                        }

                        reader.ReadEndArray();
                        break;
                    }

                    case "sub-directory-names":
                    {
                        reader.ReadStartArray();

                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            var subDirectoryName = reader.ReadString();
                            if (subDirectoryName is not null)
                                subDirectoryNames.Add(subDirectoryName);
                        }

                        reader.ReadEndArray();
                        break;
                    }
                }
            }
            reader.ReadEndObject();

            return new SpriteMakingHistory(sprites, subDirectoryNames);
        }


        private static void WriteSpriteHistory(Utf8JsonWriter writer, SpriteMakingHistory.SpriteHistory spriteHistory)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("output-file");
            WriteFileInfo(writer, spriteHistory.OutputFile);

            writer.WritePropertyName("input-files");
            writer.WriteStartArray();
            foreach (var inputFile in spriteHistory.InputFiles)
                WriteSpriteSourceFileInfo(writer, inputFile);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static SpriteMakingHistory.SpriteHistory ReadSpriteHistory(ref Utf8JsonReader reader)
        {
            FileInfo? outputFile = null;
            var inputFiles = new List<SpriteSourceFileInfo>();

            reader.ReadStartObject();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                switch (reader.ReadPropertyName())
                {
                    default: reader.SkipValue(); break;

                    case "output-file":
                    {
                        outputFile = ReadFileInfo(ref reader);
                        break;
                    }

                    case "input-files":
                    {
                        reader.ReadStartArray();
                        while (reader.TokenType != JsonTokenType.EndArray)
                            inputFiles.Add(ReadSpriteSourceFileInfo(ref reader));
                        reader.ReadEndArray();
                        break;
                    }
                }
            }
            reader.ReadEndObject();

            return new SpriteMakingHistory.SpriteHistory(outputFile, inputFiles.ToArray());
        }


        private static void WriteFileInfo(Utf8JsonWriter writer, FileInfo fileInfo)
        {
            writer.WriteStartObject();

            writer.WriteString("path", fileInfo.Path);
            writer.WriteNumber("file-size", fileInfo.FileSize);
            writer.WriteString("file-hash", fileInfo.FileHash.ToString());
            writer.WriteNumber("last-modified", fileInfo.LastModified.ToUnixTimeMilliseconds());

            writer.WriteEndObject();
        }

        private static FileInfo ReadFileInfo(ref Utf8JsonReader reader)
        {
            string? path = null;
            var fileSize = 0;
            var fileHash = new FileHash();
            var lastModified = DateTimeOffset.MinValue;

            reader.ReadStartObject();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                switch (reader.ReadPropertyName())
                {
                    default: reader.SkipValue(); break;
                    case "path": path = reader.ReadString(); break;
                    case "file-size": fileSize = (int)reader.ReadInt64(); break;
                    case "file-hash": fileHash = FileHash.Parse(reader.ReadString() ?? ""); break;
                    case "last-modified": lastModified = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()); break;
                }
            }
            reader.ReadEndObject();

            return new FileInfo(path ?? "", fileSize, fileHash, lastModified);
        }


        private static void WriteSpriteSourceFileInfo(Utf8JsonWriter writer, SpriteSourceFileInfo fileInfo)
        {
            writer.WriteStartObject();

            writer.WriteString("path", fileInfo.Path);
            writer.WriteNumber("file-size", fileInfo.FileSize);
            writer.WriteString("file-hash", fileInfo.FileHash.ToString());
            writer.WriteNumber("last-modified", fileInfo.LastModified.ToUnixTimeMilliseconds());
            writer.WritePropertyName("settings");
            WriteSpriteSettings(writer, fileInfo.Settings);

            writer.WriteEndObject();
        }

        private static SpriteSourceFileInfo ReadSpriteSourceFileInfo(ref Utf8JsonReader reader)
        {
            string? path = null;
            var fileSize = 0;
            var fileHash = new FileHash();
            var lastModified = DateTimeOffset.MinValue;
            SpriteSettings? settings = null;

            reader.ReadStartObject();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                switch (reader.ReadPropertyName())
                {
                    default: reader.SkipValue(); break;
                    case "path": path = reader.ReadString(); break;
                    case "file-size": fileSize = (int)reader.ReadInt64(); break;
                    case "file-hash": fileHash = FileHash.Parse(reader.ReadString() ?? ""); break;
                    case "last-modified": lastModified = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()); break;
                    case "settings": settings = ReadSpriteSettings(ref reader); break;
                }
            }
            reader.ReadEndObject();

            return new SpriteSourceFileInfo(path ?? "", fileSize, fileHash, lastModified, settings ?? new SpriteSettings());
        }


        private static void WriteSpriteSettings(Utf8JsonWriter writer, SpriteSettings settings)
        {
            writer.WriteStartObject();

            if (settings.Ignore != null) writer.WriteBoolean("ignore", settings.Ignore.Value);
            if (settings.SpriteType != null) writer.WriteString("sprite-type", Serialization.ToString(settings.SpriteType.Value));
            if (settings.SpriteTextureFormat != null) writer.WriteString("sprite-texture-format", Serialization.ToString(settings.SpriteTextureFormat.Value));
            if (settings.FrameNumber != null) writer.WriteNumber("frame-number", settings.FrameNumber.Value);
            if (settings.SpritesheetTileSize != null) writer.WriteString("spritesheet-tile-size", Serialization.ToString(settings.SpritesheetTileSize.Value));
            if (settings.FrameOffset != null) writer.WriteString("frame-offset", Serialization.ToString(settings.FrameOffset.Value));
            if (settings.DitheringAlgorithm != null) writer.WriteString("dithering-algorithm", Serialization.ToString(settings.DitheringAlgorithm.Value));
            if (settings.DitherScale != null) writer.WriteNumber("dither-scale", settings.DitherScale.Value);
            if (settings.AlphaTestTransparencyThreshold != null) writer.WriteNumber("alpha-test-transparency-threshold", settings.AlphaTestTransparencyThreshold.Value);
            if (settings.AlphaTestTransparencyColor != null) writer.WriteString("alpha-test-transparency-color", Serialization.ToString(settings.AlphaTestTransparencyColor.Value));
            if (settings.IndexAlphaTransparencySource != null) writer.WriteString("index-alpha-transparency-source", Serialization.ToString(settings.IndexAlphaTransparencySource.Value));
            if (settings.IndexAlphaColor != null) writer.WriteString("index-alpha-color", Serialization.ToString(settings.IndexAlphaColor.Value));
            if (settings.Converter != null) writer.WriteString("converter", settings.Converter);
            if (settings.ConverterArguments != null) writer.WriteString("converter-arguments", settings.ConverterArguments);

            writer.WriteEndObject();
        }

        private static SpriteSettings ReadSpriteSettings(ref Utf8JsonReader reader)
        {
            var settings = new SpriteSettings();

            reader.ReadStartObject();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                switch (reader.ReadPropertyName())
                {
                    default: reader.SkipValue(); break;
                    case "ignore": settings.Ignore = reader.ReadBoolean(); break;
                    case "sprite-type": settings.SpriteType = Serialization.ReadSpriteType(reader.ReadString()); break;
                    case "sprite-texture-format": settings.SpriteTextureFormat = Serialization.ReadSpriteTextureFormat(reader.ReadString()); break;
                    case "frame-number": settings.FrameNumber = (int)reader.ReadInt64(); break;
                    case "spritesheet-tile-size": settings.SpritesheetTileSize = Serialization.ReadSize(reader.ReadString()); break;
                    case "frame-offset": settings.FrameOffset = Serialization.ReadPoint(reader.ReadString()); break;
                    case "dithering-algorithm": settings.DitheringAlgorithm = Serialization.ReadDitheringAlgorithm(reader.ReadString()); break;
                    case "dither-scale": settings.DitherScale = reader.ReadFloat(); break;
                    case "alpha-test-transparency-threshold": settings.AlphaTestTransparencyThreshold = (int)reader.ReadInt64(); break;
                    case "alpha-test-transparency-color": settings.AlphaTestTransparencyColor = Serialization.ReadRgba32(reader.ReadString()); break;
                    case "index-alpha-transparency-source": settings.IndexAlphaTransparencySource = Serialization.ReadIndexAlphaTransparencySource(reader.ReadString()); break;
                    case "index-alpha-color": settings.IndexAlphaColor = Serialization.ReadRgba32(reader.ReadString()); break;
                    case "converter": settings.Converter = reader.ReadString(); break;
                    case "converter-arguments": settings.ConverterArguments = reader.ReadString(); break;
                }
            }
            reader.ReadEndObject();

            return settings;
        }
    }
}
