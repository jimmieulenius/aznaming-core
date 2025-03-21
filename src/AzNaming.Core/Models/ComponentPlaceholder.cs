namespace AzNaming.Core.Models;

public class ComponentPlaceholder
{
    public required string Name { get; set; }

    public bool IsOptional { get; set; }

    public required BaseComponent Component { get; set; }
}