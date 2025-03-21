namespace AzNaming.Core.Models;

public class DictionaryComponent(string name) : BaseComponent(ComponentType.Dictionary, name)
{
    public IDictionary<string, string> Source { get; set; } = new Dictionary<string, string>();
}