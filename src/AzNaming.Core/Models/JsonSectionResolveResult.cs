namespace AzNaming.Core.Models;

public class JsonSectionResolveResult<T>(T? result)
{
    public bool HasResult { get; private set; } = result is not null;

    public T? Result { get; private set; } = result;
}
