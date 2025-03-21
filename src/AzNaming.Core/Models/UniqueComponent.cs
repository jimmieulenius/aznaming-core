namespace AzNaming.Core.Models;

public class UniqueComponent(string name) : BaseComponent(ComponentType.Unique, name)
{
    public const int LengthMin = 1;

    public const int LengthMax = 32;

    public const int LengthDefault = 4;

    public int Length { get; set; } = LengthDefault;

    public string? Seed { get; set; }
}