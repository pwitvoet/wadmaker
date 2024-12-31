using System.Text.Json;

namespace Shared.JSON
{
    public static class Utf8JsonReaderExtensions
    {
        public static void ReadStartObject(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException($"Expected object start but found {reader.TokenType}.");
            reader.Read();
        }

        public static void ReadEndObject(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException($"Expected object end but found {reader.TokenType}.");
            reader.Read();
        }

        public static void ReadStartArray(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"Expected array start but found {reader.TokenType}.");
            reader.Read();
        }

        public static void ReadEndArray(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException($"Expected array end but found {reader.TokenType}.");
            reader.Read();
        }

        public static string? ReadPropertyName(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException($"Expected property name but found {reader.TokenType}.");
            var name = reader.GetString();
            reader.Read();
            return name;
        }

        public static string? ReadString(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException($"Expected string but found {reader.TokenType}.");
            var value = reader.GetString();
            reader.Read();
            return value;
        }

        public static long ReadInt64(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException($"Expected number but found {reader.TokenType}.");
            var value = reader.GetInt64();
            reader.Read();
            return value;
        }

        public static float ReadFloat(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException($"Expected number but found {reader.TokenType}.");
            var value = reader.GetSingle();
            reader.Read();
            return value;
        }

        public static bool ReadBoolean(this ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False) throw new JsonException($"Expected boolean but found {reader.TokenType}.");
            var value = reader.GetBoolean();
            reader.Read();
            return value;
        }

        public static void SkipValue(this ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    reader.Read();
                    return;

                case JsonTokenType.StartObject:
                    reader.ReadStartObject();
                    while (reader.TokenType != JsonTokenType.EndObject)
                    {
                        reader.ReadPropertyName();
                        reader.SkipValue();
                    }
                    reader.ReadEndObject();
                    return;

                case JsonTokenType.StartArray:
                    reader.ReadStartArray();
                    while (reader.TokenType != JsonTokenType.EndArray)
                        reader.SkipValue();
                    reader.ReadEndArray();
                    return;

                default:
                    throw new JsonException($"Expected value but found {reader.TokenType}.");
            }
        }
    }
}
