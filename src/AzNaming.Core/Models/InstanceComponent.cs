namespace AzNaming.Core.Models;

public class InstanceComponent(string name) : BaseComponent(ComponentType.Instance, name)
{
    public int? MinValue { get; set; }

    public required int MaxValue { get; set; }
    
    public PaddingConfig? Padding { get; set; }
}