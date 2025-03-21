namespace AzNaming.Core.Models;

public class ValuesInfo
{
    public required IDictionary<string, string?> Values { get; set; }

    public string[] ValidValuesKeys { get; set; } = [];

    public string[] InvalidValuesKeys { get; set; } = [];

    public string[] AdditionalValuesKeys { get; set; } = [];
}