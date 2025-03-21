using System.Text.Json.Nodes;

namespace AzNaming.Core.Models;

public class CachedJsonNode
{
    public required JsonNode Node { get; set; }

    public string? PropertyName { get; set; }
    
    public required string Key { get; set; }
}
