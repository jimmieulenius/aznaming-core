namespace AzNaming.Core.Models;

public class TemplateResult(string template, string result, bool success)
{
    public string Template { get; private set; } = template;

    public string Result { get; private set; } = result;

    public bool Success { get; private set; } = success;
}
