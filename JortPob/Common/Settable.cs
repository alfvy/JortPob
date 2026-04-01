using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JortPob.Common
{
    public static class Settable
    {

        private static readonly JsonNode _json;
        private static readonly JsonSerializerOptions _options;
        
        static Settable()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            if (!File.Exists(path)) throw new FileNotFoundException($"Settings file not found at {path}");
            
            _json = JsonNode.Parse(
                File.ReadAllText(path), 
                null, 
                // Allow comments in JSON for documentation
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip, 
                    AllowTrailingCommas = true
                }
            );
            
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true,
                WriteIndented = true,
                Converters = 
                { 
                    new JsonStringEnumConverter(),
                    new Vector3ArrayConverter(), // Adds support for [x,y,z]
                    new Vector2ArrayConverter(),  // Adds support for [x,y]
                    new SingleOrArrayConverter<string>() // Adds support for a single string to string array
                }
            };
        }
        
        public static void PopulateStaticClass(Type type)
        {
            JsonObject rootObject = _json.AsObject();
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static);

            foreach (PropertyInfo prop in properties)
            {
                var attribute = prop.GetCustomAttribute<SettingAttribute>();
                if (attribute == null) continue;

                // Check if the key exists in JSON
                if (!rootObject.TryGetPropertyValue(prop.Name, out JsonNode node))
                {
                    if (attribute.IsRequired)
                    {
                        throw new KeyNotFoundException($"Required setting '{prop.Name}' is missing from settings.json.");
                    }
                    else
                    {
                        // It's optional and missing. Convert the C# default value into a JSON Node.
                        // This allows it to pass through our custom converters (like Vector3) seamlessly.
                        node = JsonSerializer.SerializeToNode(attribute.DefaultValue, _options);
                    }
                }

                // Handle explicit nulls in the JSON
                if (node == null)
                {
                    Type targetType = prop.PropertyType;
                    bool isNullable = !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

                    if (!isNullable)
                    {
                        throw new InvalidDataException($"Setting '{prop.Name}' is explicitly set to null in JSON, but its C# type ({targetType.Name}) cannot be null.");
                    }

                    prop.SetValue(null, null);
                    continue;
                }

                // Deserialize normally into the property type
                try
                {
                    object parsedValue = node.Deserialize(prop.PropertyType, _options);
                    prop.SetValue(null, parsedValue);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"Failed to convert setting '{prop.Name}' to type {prop.PropertyType.Name}. JSON Error: {ex.Message}");
                }
            }
        }
        
        private class Vector3ArrayConverter : JsonConverter<Vector3>
        {
            public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected Array [x,y,z] for Vector3.");
                
                reader.Read();
                float x = (float)reader.GetDouble();
                
                reader.Read();
                float y = (float)reader.GetDouble();
                
                reader.Read();
                float z = (float)reader.GetDouble();

                reader.Read();
                if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Vector3 Array has too many elements.");

                return new Vector3(x, y, z);
            }

            public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(value.X);
                writer.WriteNumberValue(value.Y);
                writer.WriteNumberValue(value.Z);
                writer.WriteEndArray();
            }
        }

        private class Vector2ArrayConverter : JsonConverter<Vector2>
        {
            public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected Array [x,y] for Vector2.");

                reader.Read();
                float x = (float)reader.GetDouble();

                reader.Read();
                float y = (float)reader.GetDouble();

                reader.Read();
                if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Vector2 Array has too many elements.");

                return new Vector2(x, y);
            }

            public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(value.X);
                writer.WriteNumberValue(value.Y);
                writer.WriteEndArray();
            }
        }
        
        private class SingleOrArrayConverter<T> : JsonConverter<T[]>
        {
            public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var list = new List<T>();
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        list.Add(JsonSerializer.Deserialize<T>(ref reader, options));
                        reader.Read();
                    }
                    return list.ToArray();
                }

                // Not an array? Parse single item and wrap it.
                T singleItem = JsonSerializer.Deserialize<T>(ref reader, options);
                return new T[] { singleItem };
            }

            public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (var item in value)
                {
                    JsonSerializer.Serialize(writer, item, options);
                }
                writer.WriteEndArray();
            }
        }
    }
}
