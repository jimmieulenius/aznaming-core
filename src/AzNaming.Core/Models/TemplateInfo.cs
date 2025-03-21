namespace AzNaming.Core.Models;

public class TemplateInfo
{
    public required string Template { get; set; }

    public string? Key { get; set; }

    public string? ActualKey { get; set; }

    public Casing Casing { get; set; } = Casing.None;

    public int? LengthMax { get; set; }

    public int? LengthMin { get; set; }

    public required ComponentPlaceholder[] Placeholders { get; set; }

    public bool AllowTruncation { get; set; }

    public string? ValidText { get; set; }

    public string? InvalidText { get; set; }

    public string? InvalidCharacters { get; set; }

    public string? InvalidCharactersStart { get; set; }

    public string? InvalidCharactersEnd { get; set; }

    public string? InvalidCharactersConsecutive { get; set; }

    public string? Regex { get; set; }

    public string? StaticValue { get; set; }
}