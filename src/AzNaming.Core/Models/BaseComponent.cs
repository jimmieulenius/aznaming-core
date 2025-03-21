namespace AzNaming.Core.Models;

public abstract class BaseComponent(ComponentType type, string name)
{
    public ComponentType Type { get; protected set; } = type;
    
    public string Name { get; protected set; } = name;

    public Casing Casing { get; set; }
}
