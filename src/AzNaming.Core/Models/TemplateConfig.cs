namespace AzNaming.Core.Models;

public class TemplateConfig
{
    public string? Key { get; set; }

    public string? Name { get; set; }

    public string? ShortName { get; set; }

    public string? Template { get; set; }

    public Dictionary<string, object> Values { get; set; } = [];

    public int? LengthMax { get; set; }

    public int? LengthMin { get; set; }

    public Casing? Casing { get; set; }

    public string? ValidText { get; set; }

    public string? InvalidText { get; set; }

    public string? InvalidCharacters { get; set; }

    public string? InvalidCharactersStart { get; set; }

    public string? InvalidCharactersEnd { get; set; }

    public string? InvalidCharactersConsecutive { get; set; }

    public string? Regex { get; set; }

    public string? StaticValue { get; set; }
}
