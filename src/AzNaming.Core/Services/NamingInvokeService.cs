using System.Text.Json;
using AzNaming.Core.Models;

namespace AzNaming.Core.Services;

public class NamingInvokeService(params string[] configUri)
{
    private string[] _configUri = configUri;

    public InvokeResult<string> InvokeEvaluateName(string keyOrTemplate, string valuesJsonOrPath, string? optionsJson = null)
    {
        NameInfo? result = default;
        string? error = default;

        InvokeNamingService(namingService =>
            {
                result = namingService.EvaluateName(keyOrTemplate, GetJson(valuesJsonOrPath)!, out error);
            },
            DeserializeOptions(optionsJson));

        return new InvokeResult<string>(JsonSerializer.Serialize(result!, SourceGenerationContext.Default.NameInfo), error);
    }

    public InvokeResult<string> InvokeEvaluateNameJson(string graphJsonOrPath, string valuesJsonOrPath, string? keyOrTemplate, string? optionsJson = null)
    {
        var result = string.Empty;
        string?[] errors = [];

        InvokeNamingService(namingService =>
            {
                result = namingService.EvaluateNameJson(GetJson(graphJsonOrPath)!, GetJson(valuesJsonOrPath)!, out errors, keyOrTemplate);
            },
            DeserializeOptions(optionsJson));

        return new InvokeResult<string>(result, errors);
    }

    public InvokeResult<string> InvokeGetName(string keyOrTemplate, string valuesJsonOrPath, string? optionsJson = null)
    {
        var result = string.Empty;
        string? error = default;

        InvokeNamingService(namingService =>
            {
                result = namingService.GetName(keyOrTemplate, GetJson(valuesJsonOrPath)!, out error);
            },
            DeserializeOptions(optionsJson));

        return new InvokeResult<string>(result, error);
    }

    public InvokeResult<string> InvokeGetNameJson(string graphJsonOrPath, string valuesJsonOrPath, string? keyOrTemplate = default, string? optionsJson = null)
    {
        var result = string.Empty;
        string?[] errors = [];

        InvokeNamingService(namingService =>
            {
                result = namingService.GetNameJson(GetJson(graphJsonOrPath)!, GetJson(valuesJsonOrPath)!, out errors, keyOrTemplate);
            },
            DeserializeOptions(optionsJson));

        return new InvokeResult<string>(result, errors);
    }

    public InvokeResult<string> InvokeTestNameJson(string valuesJsonOrPath, string[]? keyOrTemplate = default, string? optionsJson = null)
    {
        var result = string.Empty;
        string?[] errors = [];

        InvokeNamingService(namingService =>
            {
                result = namingService.TestNameJson(GetJson(valuesJsonOrPath)!, out errors, keyOrTemplate ?? []);
            },
            DeserializeOptions(optionsJson));

        return new InvokeResult<string>(result, errors);
    }

    private static NamingInvokeOptions? DeserializeOptions(string? optionsJson)
    {
        return (optionsJson is not null) ? JsonSerializer.Deserialize(optionsJson, SourceGenerationContext.Default.NamingInvokeOptions) : default;
    }

    private static string? GetJson(string? jsonOrPath)
    {
        if (!string.IsNullOrEmpty(jsonOrPath))
        {
            var filePath = !string.IsNullOrEmpty(Path.GetPathRoot(jsonOrPath))
                ? jsonOrPath
                : Path.Combine(Environment.CurrentDirectory!, jsonOrPath!);

            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
        }

        return jsonOrPath;
    }

    private void InvokeNamingService(Action<NamingService> action, NamingInvokeOptions? options = null)
    {
        using var fileService = new FileService();
        using var azureRestService = new AzureRestService();

        try
        {
            var namingService = new NamingService(new JsonService(fileService), azureRestService, _configUri);

            if (options is null)
            {
                options = new NamingInvokeOptions();
            }

            if (options.ClearConfig)
            {
                namingService.ClearConfig();
            }

            namingService.NoAdditionalValues = options.NoAdditionalValues;
            namingService.CheckUniqueName = options.CheckUniqueName;
            namingService.AllowTruncation = options.AllowTruncation;
            namingService.SuppressError = options.SuppressError;
            namingService.SubscriptionId = options.SubscriptionId;
            namingService.ResourceGroupName = options.ResourceGroupName;
            namingService.Location = options.Location;

            var configUri = new List<string>();
            var addConfigUri = new Action<string?>(item =>
            {
                if (!string.IsNullOrEmpty(item))
                {
                    item.Split(",;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList().ForEach(subItem =>
                    {
                        if (!configUri.Contains(subItem))
                        {
                            configUri.Add(subItem);
                        }
                    });
                }
            });

            addConfigUri(Environment.GetEnvironmentVariable("AZURE_NAMING_CONFIG_URI"));
            options.ConfigUri.ToList().ForEach(addConfigUri);

            if (configUri.Count > 0)
            {
                foreach (var configUriItem in configUri)
                {
                    namingService.AddConfig(configUriItem);
                }
            }

            action(namingService);
        }
        // Uncomment below to not show whole stacktrace
        // catch (Exception exception)
        // {
        //     throw exception;
        // }
        finally
        {
            fileService.Dispose();
            azureRestService.Dispose();
        }
    }
}
