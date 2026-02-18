using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace osu.Server.Spectator.PublicAPISchemaExporter
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            await generateJsonSchemas();

            await Docfx.Dotnet.DotnetApiCatalog.GenerateManagedReferenceYamlFiles("docfx.json");
            await Docfx.Docset.Build("docfx.json");
        }

        private static async Task generateJsonSchemas()
        {
            var apiDir = Directory.CreateDirectory("referee-api");
            var overwritesDir = apiDir.CreateSubdirectory("overwrites");
            var schemasDir = apiDir.CreateSubdirectory("schemas");

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };

            foreach (var type in typeof(osu.Server.Spectator.Program).Assembly.GetTypes())
            {
                if (type.Namespace?.StartsWith("osu.Server.Spectator.Hubs.Referee.Models", StringComparison.Ordinal) != true)
                    continue;

                if (!type.IsPublic)
                    return;

                string jsonSchema = jsonOptions.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions
                {
                    TreatNullObliviousAsNonNullable = true,
                    TransformSchemaNode = (ctx, schema) =>
                    {
                        // `PropertyInfo` is not null only on type definitions.
                        if (ctx.PropertyInfo != null)
                            return schema;

                        if (schema is JsonObject jsonObject)
                        {
                            // `$ref` is null if a given type is encountered the first time in the schema.
                            // subsequent occurrences of the same type will not repeat the definition and instead use relative `$ref`.
                            if (jsonObject["$ref"]?.GetValue<string>() == null)
                            {
                                jsonObject.Add("$id", $"https://spectator.ppy.sh/docs/referee-api/schemas/{ctx.TypeInfo.Type.Name}.json");
                                jsonObject.Add("title", ctx.TypeInfo.Type.Name);

                                // `System.Text.Json`'s default "required" handling is based on... whether the property is required in a constructor
                                // (https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema)
                                // adding constructors to JSON types is historically a pretty bad idea because it invites transpositional confusion when deserialising.
                                // therefore we have to roll this ourselves.

                                bool isOptionalProperty(KeyValuePair<string, JsonNode?> property)
                                {
                                    if ((property.Value?["type"] as JsonArray)?.Any(t => t?.GetValue<string>() == "null") == true)
                                        return true;

                                    if ((property.Value?["enum"] as JsonArray)?.Any(t => t == null) == true)
                                        return true;

                                    return false;
                                }

                                var requiredProperties = jsonObject["properties"]?
                                                         .AsObject()
                                                         .Where(property => !isOptionalProperty(property))
                                                         .Select(property => (JsonNode)property.Key)
                                                         .ToArray() ?? [];
                                jsonObject["required"] = new JsonArray(requiredProperties);
                            }

                            // additionally mark no extra properties.
                            // technically we won't care, but we also don't want to *present* the appearance that we accept more in the schema, either.
                            if (jsonObject["type"]?.GetValue<string>() == "object")
                                jsonObject.Add("additionalProperties", false);
                        }

                        return schema;
                    }
                }).ToJsonString(jsonOptions);

                string schemaPath = Path.Combine(schemasDir.FullName, $"{type.Name}.json");
                await File.WriteAllTextAsync(schemaPath, jsonSchema);

                string overwritePath = Path.Combine(overwritesDir.FullName, $"{type.Name}.md");
                await File.WriteAllTextAsync(overwritePath,
                    $"""
                     ---
                     uid: {type.FullName}
                     remarks: *content
                     ---
                     JSON Schema for this type:

                     ```json
                     {jsonSchema}
                     ```
                     """);
            }
        }
    }
}
