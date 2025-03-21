namespace AzNaming.Core.Models;

public class NameInfo
{
    public TemplateInfo Template { get; private set; }

    public ValuesInfo Values { get; private set; }

    public string Result { get; private set; }

    public string FullResult { get; private set; }

    public bool AllowAdditionalValues { get; private set; }

    public bool Success { get; private set; }

    public string? Error { get; private set; }

    public NameInfo(TemplateInfo template, ValuesInfo values, string result, string? fullResult = null, bool allowAdditionalValues = true, string? error = null)
    {
        Template = template;
        Values = values;
        Result = result;
        FullResult = fullResult ?? result;
        AllowAdditionalValues = allowAdditionalValues;
        Error = error;
        Success = string.IsNullOrEmpty(Error);
    }
}