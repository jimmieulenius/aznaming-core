using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AzNaming.Core.Models;

namespace AzNaming.Core;

[JsonSourceGenerationOptions(
    Converters = new[] { typeof(JsonStringEnumConverter<Casing>), typeof(JsonStringEnumConverter<ComponentType>) },
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(NamingInvokeOptions))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonNode>))]
[JsonSerializable(typeof(Dictionary<string, JsonNode>))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(NameInfo))]
[JsonSerializable(typeof(ChildDictionaryComponent))]
[JsonSerializable(typeof(DictionaryComponent))]
[JsonSerializable(typeof(InstanceComponent))]
[JsonSerializable(typeof(FreeTextComponent))]
[JsonSerializable(typeof(UniqueComponent))]
[JsonSerializable(typeof(TemplateConfig))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
