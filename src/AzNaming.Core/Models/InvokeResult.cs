namespace AzNaming.Core.Models;

public class InvokeResult<T>(T result, params string?[] errors)
{
    public T Result { get; private set; } = result;

    public string?[] Errors { get; private set; } = errors;
}
