using Shared.FileSystem;
using Shared;
using System.Text.Json.Serialization;
using System.Text.Json;
using Shared.JSON;
using FileInfo = Shared.FileSystem.FileInfo;

namespace WadMaker.Settings
{
    class WadMakingHistoryJsonSerializer : JsonConverter<WadMakingHistory>
    {
        public override void Write(Utf8JsonWriter writer, WadMakingHistory value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("output-file");
            WriteFileInfo(writer, value.OutputFile);

            writer.WritePropertyName("texture-inputs");
            writer.WriteStartObject();
            foreach (var inputs in value.TextureInputs)
            {
                writer.WritePropertyName(inputs.Key);
                writer.WriteStartArray();

                foreach (var textureSourceFileInfo in inputs.Value)
                    WriteTextureSourceFileInfo(writer, textureSourceFileInfo);

                writer.WriteEndArray();
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        public override WadMakingHistory? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var outputFile = new FileInfo("", 0, new FileHash(), DateTimeOffset.MinValue);
            var textureInputs = new Dictionary<string, TextureSourceFileInfo[]>();

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

                    case "texture-inputs":
                    {
                        reader.ReadStartObject();
                        while (reader.TokenType != JsonTokenType.EndObject)
                        {
                            var key = reader.ReadPropertyName() ?? "";

                            var items = new List<TextureSourceFileInfo>();
                            reader.ReadStartArray();

                            while (reader.TokenType != JsonTokenType.EndArray)
                                items.Add(ReadTextureSourceFileInfo(ref reader));

                            reader.ReadEndArray();
                            textureInputs[key] = items.ToArray();
                        }
                        reader.ReadEndObject();
                        break;
                    }
                }
            }
            reader.ReadEndObject();

            return new WadMakingHistory(outputFile, textureInputs);
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


        private static void WriteTextureSourceFileInfo(Utf8JsonWriter writer, TextureSourceFileInfo fileInfo)
        {
            writer.WriteStartObject();

            writer.WriteString("path", fileInfo.Path);
            writer.WriteNumber("file-size", fileInfo.FileSize);
            writer.WriteString("file-hash", fileInfo.FileHash.ToString());
            writer.WriteNumber("last-modified", fileInfo.LastModified.ToUnixTimeMilliseconds());
            writer.WritePropertyName("settings");
            WriteTextureSettings(writer, fileInfo.Settings);

            writer.WriteEndObject();
        }

        private static TextureSourceFileInfo ReadTextureSourceFileInfo(ref Utf8JsonReader reader)
        {
            string? path = null;
            var fileSize = 0;
            var fileHash = new FileHash();
            var lastModified = DateTimeOffset.MinValue;
            TextureSettings? settings = null;

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
                    case "settings": settings = ReadTextureSettings(ref reader); break;
                }
            }
            reader.ReadEndObject();

            return new TextureSourceFileInfo(path ?? "", fileSize, fileHash, lastModified, settings ?? new TextureSettings());
        }


        private static void WriteTextureSettings(Utf8JsonWriter writer, TextureSettings settings)
        {
            writer.WriteStartObject();

            if (settings.Ignore != null) writer.WriteBoolean("ignore", settings.Ignore.Value);
            if (settings.TextureType != null) writer.WriteString("texture-type", Serialization.ToString(settings.TextureType.Value));
            if (settings.MipmapLevel != null) writer.WriteString("mipmap-level", Serialization.ToString(settings.MipmapLevel.Value));
            if (settings.DitheringAlgorithm != null) writer.WriteString("dithering-algorithm", Serialization.ToString(settings.DitheringAlgorithm.Value));
            if (settings.DitherScale != null) writer.WriteNumber("dither-scale", settings.DitherScale.Value);
            if (settings.TransparencyThreshold != null) writer.WriteNumber("transparency-threshold", settings.TransparencyThreshold.Value);
            if (settings.TransparencyColor != null) writer.WriteString("transparency-color", Serialization.ToString(settings.TransparencyColor.Value));
            if (settings.WaterFogColor != null) writer.WriteString("water-fog-color", Serialization.ToString(settings.WaterFogColor.Value));
            if (settings.DecalTransparencySource != null) writer.WriteString("decal-transparency-source", Serialization.ToString(settings.DecalTransparencySource.Value));
            if (settings.DecalColor != null) writer.WriteString("decal-color", Serialization.ToString(settings.DecalColor.Value));
            if (settings.Converter != null) writer.WriteString("converter", settings.Converter);
            if (settings.ConverterArguments != null) writer.WriteString("converter-arguments", settings.ConverterArguments);

            writer.WriteEndObject();
        }

        private static TextureSettings ReadTextureSettings(ref Utf8JsonReader reader)
        {
            var settings = new TextureSettings();

            reader.ReadStartObject();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                switch (reader.ReadPropertyName())
                {
                    default: reader.SkipValue(); break;
                    case "ignore": settings.Ignore = reader.ReadBoolean(); break;
                    case "texture-type": settings.TextureType = Serialization.ReadTextureType(reader.ReadString()); break;
                    case "mipmap-level": settings.MipmapLevel = Serialization.ReadMipmapLevel(reader.ReadString()); break;
                    case "dithering-algorithm": settings.DitheringAlgorithm = Serialization.ReadDitheringAlgorithm(reader.ReadString()); break;
                    case "dither-scale": settings.DitherScale = reader.ReadFloat(); break;
                    case "transparency-threshold": settings.TransparencyThreshold = (int)reader.ReadInt64(); break;
                    case "transparency-color": settings.TransparencyColor = Serialization.ReadRgba32(reader.ReadString()); break;
                    case "water-fog-color": settings.WaterFogColor = Serialization.ReadRgba32(reader.ReadString()); break;
                    case "decal-transparency-source": settings.DecalTransparencySource = Serialization.ReadDecalTransparencySource(reader.ReadString()); break;
                    case "decal-color": settings.DecalColor = Serialization.ReadRgba32(reader.ReadString()); break;
                    case "converter": settings.Converter = reader.ReadString(); break;
                    case "converter-arguments": settings.ConverterArguments = reader.ReadString(); break;
                }
            }
            reader.ReadEndObject();

            return settings;
        }
    }
}
