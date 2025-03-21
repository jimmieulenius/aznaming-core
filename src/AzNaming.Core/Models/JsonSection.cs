using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzNaming.Core.Models;

public class JsonSection
{
    public string Id { get; private set; }

    public string Uri { get; private set; }

    public string? Path { get; private set; }

    public Lazy<JsonSectionResolveResult<Lazy<JsonNode>>> Result { get; private set; }

    public Lazy<IDictionary<string, string>> IdPaths { get; private set; }

    public ConcurrentDictionary<string, CachedJsonNode> NodeCache { get; set; }

    public JsonSection(string uri, Func<JsonSection, JsonSectionResolveResult<Lazy<JsonNode>>> factory, string? path = null)
    {
        Id = Guid.NewGuid().ToString();
        Uri = uri;
        Path = path;
        Result = new Lazy<JsonSectionResolveResult<Lazy<JsonNode>>>(() => factory(this));
        IdPaths = new Lazy<IDictionary<string, string>>(GetIdPaths);
        NodeCache = new ConcurrentDictionary<string, CachedJsonNode>();
    }

    private Dictionary<string, string> GetIdPaths()
    {
        var result = new Dictionary<string, string>();

        void AddPaths(JsonNode? node)
        {
            if (node is null)
            {
                return;
            }

            if (node is JsonObject objectNode)
            {
                foreach (var property in objectNode)
                {
                    switch (property.Key)
                    {
                        case "$id":
                            if (property.Value is not null)
                            {
                                var id = property.Value.Deserialize(SourceGenerationContext.Default.String);

                                if (!string.IsNullOrEmpty(id))
                                {
                                    result.Add(id, node.GetPath());
                                }
                            }
                            break;
                        default:
                            AddPaths(property.Value);
                            break;
                    }
                }
            }
            else if (node is JsonArray arrayNode)
            {
                foreach (var item in arrayNode)
                {
                    AddPaths(item);
                }
            }
        }

        if (Result.Value.HasResult)
        {
            AddPaths(Result.Value.Result!.Value);
        }

        return result;
    }
}
