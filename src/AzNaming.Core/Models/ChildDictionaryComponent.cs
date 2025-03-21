namespace AzNaming.Core.Models;

public class ChildDictionaryComponent(string name) : BaseComponent(ComponentType.Dictionary, name)
{
    public required string Parent { get; set; }
    
    public IDictionary<string, IDictionary<string, string>> Source { get; set; } = new Dictionary<string, IDictionary<string, string>>();
}