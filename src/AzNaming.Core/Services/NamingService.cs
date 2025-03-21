using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AzNaming.Core.Extensions;
using AzNaming.Core.Models;

namespace AzNaming.Core.Services;

public partial class NamingService
{
    private readonly JsonService _jsonService;

    private readonly AzureRestService _azureRestService;

    private readonly ConcurrentDictionary<string, BaseComponent> _componentCache = new();

    private readonly ConcurrentDictionary<string, TemplateConfig> _templateCache = new();

    private readonly ConcurrentDictionary<string, string> _aliasCache = new();

    private Lazy<Dictionary<string, string[]>> _componentAliasReferences;

    private Lazy<Dictionary<string, string[]>> _templateAliasReferences;

    public bool NoAdditionalValues { get; set; }

    public bool AllowTruncation { get; set; }

    public bool SuppressError { get; set; }

    public bool CheckUniqueName { get; set; }

    public string? SubscriptionId { get; set; }

    public string? ResourceGroupName { get; set; }

    public string? Location { get; set; }

    [GeneratedRegex(@"{(.*?)}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\[(.*?)\]")]
    private static partial Regex OptionalPlaceholderRegex();

    public NamingService(JsonService jsonService, AzureRestService azureRestService, params string[] configUri)
    {
        _jsonService = jsonService;
        _azureRestService = azureRestService;

        foreach (var configUriItem in configUri)
        {
            AddConfig(configUriItem);
        }

        _componentAliasReferences = new(() => GetAliasReferences("$.aliases.components"));
        _templateAliasReferences = new(() => GetAliasReferences("$.aliases.templates"));
    }

    public string GetName(string keyOrTemplate, Dictionary<string, object> values, out string? error)
    {
        var result = GetNameInfo(keyOrTemplate, values);
        error = result.Error;

        return result.Result;
    }

    public string GetName(string keyOrTemplate, string valuesJson, out string? error)
    {
        return GetName(keyOrTemplate, _jsonService.Deserialize(valuesJson, SourceGenerationContext.Default.DictionaryStringObject)!, out error);
    }

    public string GetNameJson(string graphJson, string valuesJson, out string?[] error, string? keyOrTemplate = default)
    {
        var graph = _jsonService.Deserialize(graphJson, SourceGenerationContext.Default.JsonNode)!;
        var values = (_jsonService.Deserialize(valuesJson, SourceGenerationContext.Default.DictionaryStringObject) ?? []).ToDictionary(item => item.Key.ToLowerInvariant(), item => item.Value);
        var errors = new List<string?>();
        var currentSettings = new
        {
            SuppressError
        };

        SuppressError = true;

        EnumerateGraph(
            (node, template, values) => {
                var result = GetName(template!, values, out var error);

                if (!string.IsNullOrEmpty(error))
                {
                    errors.Add(error);
                }

                return _jsonService.ToJsonNode(result);
            },
            graph,
            values,
            keyOrTemplate
        );

        SuppressError = currentSettings.SuppressError;

        error = [.. errors];

        if (!SuppressError && errors.Count > 0)
        {
            throw new AggregateException(errors.Select(item => new Exception(item)));
        }

        return _jsonService.Serialize(graph, SourceGenerationContext.Default.JsonNode);
    }

    public NameInfo EvaluateName(string keyOrTemplate, Dictionary<string, object> values, out string? error)
    {
        var currentSettings = new
        {
            SuppressError
        };

        SuppressError = true;

        var result = GetNameInfo(keyOrTemplate, values);
        error = result.Error;

        SuppressError = currentSettings.SuppressError;

        return result;
    }

    public NameInfo EvaluateName(string keyOrTemplate, string valuesJson, out string? error)
    {
        return EvaluateName(keyOrTemplate, _jsonService.Deserialize(valuesJson, SourceGenerationContext.Default.DictionaryStringObject)!, out error);
    }

    public string EvaluateNameJson(string graphJson, string valuesJson, out string?[] error, string? keyOrTemplate = default)
    {
        var result = new Dictionary<string, JsonNode>();
        var graph = _jsonService.Deserialize(graphJson, SourceGenerationContext.Default.JsonNode)!;
        var values = _jsonService.Deserialize(valuesJson, SourceGenerationContext.Default.DictionaryStringObject) ?? [];
        var errors = new List<string?>();

        EnumerateGraph(
            (node, template, values) => {
                result.Add(node.GetPath(), _jsonService.Deserialize(_jsonService.Serialize(EvaluateName(template!, values, out var error), SourceGenerationContext.Default.NameInfo), SourceGenerationContext.Default.JsonNode)!);

                if (!string.IsNullOrEmpty(error))
                {
                    errors.Add(error);
                }

                return null;
            },
            graph,
            values,
            keyOrTemplate
        );

        error = [.. errors];

        return _jsonService.Serialize(result, SourceGenerationContext.Default.DictionaryStringJsonNode);
    }

    public bool TestName(Dictionary<string, object> values, out string?[] error, params string[] keyOrTemplate)
    {
        var errors = new List<string?>();

        void _TestName(string keyOrTemplateItem)
        {
            var nameInfo = EvaluateName(keyOrTemplateItem, values, out _);

            if (!string.IsNullOrEmpty(nameInfo.Error))
            {
                errors.Add(nameInfo.Error);
            }
        }

        if (keyOrTemplate.Length > 0)
        {
            foreach (var keyOrTemplateItem in keyOrTemplate)
            {
                _TestName(keyOrTemplateItem);
            }
        }
        else
        {
            string[] specialPropertyName = [
                "$id",
                "default"
            ];

            if (_jsonService.TryGetNode("$.templates", out var templatesNode))
            {
                foreach (var templatesNodeItem in templatesNode!.AsObject())
                {
                    if (!specialPropertyName.Contains(templatesNodeItem.Key))
                    {
                        _TestName(templatesNodeItem.Key);
                    }
                }
            }
        }

        error = [.. errors];

        return errors.Count == 0;
    }

    public string TestNameJson(string valuesJson, out string?[] error, params string[] keyOrTemplate)
    {
        var result = TestName(_jsonService.Deserialize(valuesJson, SourceGenerationContext.Default.DictionaryStringObject)!, out error, keyOrTemplate);

        return _jsonService.Serialize(new Dictionary<string, object>()
        {
            { "success", result },
            { "errors", error }
        }, SourceGenerationContext.Default.DictionaryStringObject);
    }

    private Dictionary<string, string[]> GetAliasReferences(string path)
    {
        var result = new Dictionary<string, string[]>();

        if (_jsonService.TryGetValue(path, out var alias, SourceGenerationContext.Default.DictionaryStringString))
        {
            foreach (var aliasItem in alias!)
            {
                var key = aliasItem.Key.ToLowerInvariant();
                var value = aliasItem.Value.ToLowerInvariant();

                if (result.TryGetValue(value, out var values))
                {
                    if (!values.Contains(key))
                    {
                        result[value] = [.. values, key];
                    }
                }
                else
                {
                    result.Add(value, [key]);
                }
            }
        }

        return result;
    }

    public void AddConfig(string uri)
    {
        _jsonService.InsertSection(0, uri);
    }

    public void ClearConfig()
    {
        _jsonService.ClearSections();
    }

    public void EnumerateGraph(Func<JsonNode, string?, Dictionary<string, object>, JsonNode?> action, JsonNode graph, Dictionary<string, object> values, string? keyOrTemplate = default)
    {
        var specialProperty = new
        {
            Template = "template",
            Values = "values"
        };
        Dictionary<string, Stack<JsonNode>> nodeStack = [];

        bool PushStack(string key, JsonNode? node)
        {
            if (node is JsonObject objectNode)
            {
                if (objectNode.TryGetPropertyValue(key, out var value))
                {
                    if (value is not null)
                    {
                        if (nodeStack.TryGetValue(key, out var stack))
                        {
                            stack.Push(value);
                        }
                        else
                        {
                            nodeStack.Add(key, new Stack<JsonNode>([value]));
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        void PopStack(string key)
        {
            if (nodeStack.TryGetValue(key, out var stackValue))
            {
                if (stackValue.Count > 0)
                {
                    stackValue.Pop();
                }
            }
        }

        string? GetTemplate(JsonNode node, string? template)
        {
            var result = template;
            var key = specialProperty.Template;

            if (node is JsonObject objectNode)
            {
                if (objectNode.TryGetPropertyValue(key, out var valueNode))
                {
                    if (valueNode?.GetValueKind() is JsonValueKind.String)
                    {
                        result = valueNode.Deserialize(SourceGenerationContext.Default.String) ?? template;
                    }
                }
            }

            if (nodeStack.TryGetValue(key, out var stackValue))
            {
                foreach (var stackValueItem in stackValue)
                {
                    if (string.IsNullOrEmpty(result))
                    {
                        result = stackValueItem.Deserialize(SourceGenerationContext.Default.String);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return result;
        }

        Dictionary<string, object> GetValues(JsonNode node, Dictionary<string, object> values)
        {
            var result = new Dictionary<string, object>();
            var key = specialProperty.Values;

            if (node is JsonObject objectNode)
            {
                if (objectNode.TryGetPropertyValue(key, out var valueNode))
                {
                    if (valueNode is JsonObject valueObject)
                    {
                        result = (valueObject.Deserialize(SourceGenerationContext.Default.DictionaryStringObject) ?? []).ToDictionary(item => item.Key.ToLowerInvariant(), item => item.Value);
                    }
                }
            }

            if (nodeStack.TryGetValue(key, out var stackValue))
            {
                foreach (var stackValueItem in stackValue)
                {
                    foreach (var valuesItem in (stackValueItem as JsonObject)!)
                    {
                        var itemKey = valuesItem.Key.ToLowerInvariant();

                        if (!result.ContainsKey(itemKey))
                        {
                            result[itemKey] = valuesItem.Value!;
                        }
                    }
                }
            }

            foreach (var valuesItem in values)
            {
                var itemKey = valuesItem.Key.ToLowerInvariant();

                if (!result.ContainsKey(itemKey))
                {
                    result[itemKey] = valuesItem.Value;
                }
            }

            return result;
        }

        bool IsNameNode(JsonNode node)
        {
            if (node is JsonObject objectNode)
            {
                return (
                    objectNode.ContainsKey(specialProperty.Template)
                    || objectNode.ContainsKey(specialProperty.Values)
                ) && !objectNode.Any(item =>
                    !item.Key.Equals(specialProperty.Template, StringComparison.InvariantCultureIgnoreCase)
                    && !item.Key.Equals(specialProperty.Values, StringComparison.InvariantCultureIgnoreCase)
                );
            }

            return false;
        }

        void InvokeAction(JsonNode node, string? keyOrTemplate, Dictionary<string, object> values)
        {
            if (node is JsonObject objectNode)
            {
                var valuesPushed = PushStack(specialProperty.Values, objectNode);
                var templatePushed = PushStack(specialProperty.Template, objectNode);

                if (IsNameNode(node))
                {
                    action(node, GetTemplate(node, keyOrTemplate), GetValues(objectNode, values));
                }

                var nodesToUpdate = new Dictionary<string, JsonNode>();

                foreach (var property in objectNode)
                {
                    if (property.Value is not null)
                    {
                        var propertyValuesPushed = PushStack(specialProperty.Values, property.Value);
                        var propertyTemplatePushed = PushStack(specialProperty.Template, property.Value);
                        
                        if (IsNameNode(property.Value)) {
                            var resolvedNode = action(property.Value, GetTemplate(property.Value, keyOrTemplate), GetValues(property.Value, values));

                            if (resolvedNode is not null)
                            {
                                nodesToUpdate.Add(property.Key, resolvedNode.DeepClone());
                            }
                        }
                        else
                        {
                            InvokeAction(property.Value, GetTemplate(property.Value, keyOrTemplate), GetValues(property.Value, values));
                        }

                        if (propertyValuesPushed)
                        {
                            PopStack(specialProperty.Values);

                            ((JsonObject)property.Value).Remove(specialProperty.Values);
                        }

                        if (propertyTemplatePushed)
                        {
                            PopStack(specialProperty.Template);

                            ((JsonObject)property.Value).Remove(specialProperty.Template);
                        }
                    }
                }

                foreach (var nodesToUpdateItem in nodesToUpdate)
                {
                    objectNode[nodesToUpdateItem.Key] = nodesToUpdateItem.Value;
                }

                if (valuesPushed)
                {
                    PopStack(specialProperty.Values);

                    objectNode.Remove(specialProperty.Values);
                }

                if (templatePushed)
                {
                    PopStack(specialProperty.Template);

                    objectNode.Remove(specialProperty.Template);
                }
            }
            else if (node is JsonArray arrayNode)
            {
                for (var index = 0; index < arrayNode.Count; index++)
                {
                    var arrayItem = arrayNode[index];

                    if (arrayItem is not null)
                    {
                        var valuesPushed = PushStack(specialProperty.Values, arrayItem);
                        var templatePushed = PushStack(specialProperty.Template, arrayItem);

                        if (IsNameNode(arrayItem)) {
                            var resolvedNode = action(arrayItem, GetTemplate(arrayItem, keyOrTemplate), GetValues(arrayItem, values));

                            if (resolvedNode is not null)
                            {
                                arrayNode[index] = resolvedNode.DeepClone();
                            }
                        }
                        else
                        {
                            InvokeAction(arrayItem, GetTemplate(arrayItem, keyOrTemplate), GetValues(arrayItem, values));
                        }

                        if (valuesPushed)
                        {
                            PopStack(specialProperty.Values);
                        }

                        if (templatePushed)
                        {
                            PopStack(specialProperty.Template);
                        }
                    }
                }
            }
        }

        InvokeAction(graph, keyOrTemplate, values);
    }

    private string ResolveComponentAlias(string name)
    {
        if (TryResolveAlias($"$.aliases.components.{name}", out var alias))
        {
            return alias!;
        }

        return name;
    }

    private string ResolveTemplateAlias(string name)
    {
        if (TryResolveAlias($"$.aliases.templates.{name}", out var alias))
        {
            return alias!;
        }

        return name;
    }

    private bool TryResolveAlias(string path, out string? name)
    {
        name = default;
        path = path.ToLowerInvariant();

        if (_aliasCache.TryGetValue(path, out var cachedAlias))
        {
            name = cachedAlias;
        }

        if (_jsonService.TryGetValue(path, out name, SourceGenerationContext.Default.String))
        {
            var value = name!.ToLowerInvariant();
            _aliasCache.AddOrUpdate(path, value!, (_, _) => value!);
            name = value;

            return true;
        }

        return false;
    }

    private NameInfo GetNameInfo(string keyOrTemplate, Dictionary<string, object> values)
    {
        string? error = default;

        Dictionary<string, ComponentPlaceholder> GetPlaceholders(string template)
        {
            var result = new Dictionary<string, ComponentPlaceholder>();
            var placeholders = PlaceholderRegex().Matches(template).ToArray();
            var optionalPlaceholder = new List<string>();

            foreach (var optionalItem in OptionalPlaceholderRegex().Matches(template).ToArray())
            {
                foreach (var optionalComponentItem in PlaceholderRegex().Matches(optionalItem.Groups[1].Value).ToArray())
                {
                    if (string.IsNullOrEmpty(optionalComponentItem.Groups[1].Value))
                    {
                        HandleException(new InvalidOperationException("Placeholder cannot be empty"), ref error);
                    }

                    var value = ResolveComponentAlias(optionalComponentItem.Groups[1].Value.ToLowerInvariant());

                    if (!optionalPlaceholder.Contains(value))
                    {
                        optionalPlaceholder.Add(value);
                    }

                    template = template.Replace(optionalItem.Groups[0].Value, null);
                }
            }

            foreach (var placeholder in PlaceholderRegex().Matches(template).ToArray())
            {
                if (string.IsNullOrEmpty(placeholder.Groups[1].Value))
                {
                    HandleException(new InvalidOperationException("Placeholder cannot be empty"), ref error);
                }

                optionalPlaceholder.Remove(ResolveComponentAlias(placeholder.Groups[1].Value.ToLowerInvariant()));
            }

            foreach (var placeholdersItem in placeholders)
            {
                var name = ResolveComponentAlias(placeholdersItem.Groups[1].Value);
                var nameLower = name.ToLowerInvariant();

                if (result.ContainsKey(nameLower))
                {
                    continue;
                }

                var component = GetComponent(name, ref error);

                if (component is not null)
                {
                    result.Add(
                        nameLower,
                        new ComponentPlaceholder()
                        {
                            Name = name,
                            IsOptional = optionalPlaceholder.Contains(nameLower),
                            Component = component
                        }
                    );
                }
                else
                {
                    HandleException(new InvalidOperationException($"Placeholder '{name}' refers to non-existing component."), ref error);
                }
            }

            return result;
        }

        Dictionary<string, string?> ResolveValues(TemplateConfig templateConfig, Dictionary<string, ComponentPlaceholder> placeholders, Dictionary<string, object> values)
        {
            var result = new Dictionary<string, string?>();
            var valuesClone = values.ToDictionary(item => item.Key.ToLowerInvariant(), item => item.Value);

            void _ResolveValues(Dictionary<string, object> values)
            {
                var isValidValue = false;

                foreach (var valuesItem in values)
                {
                    var key = ResolveComponentAlias(valuesItem.Key).ToLowerInvariant();

                    if (placeholders.TryGetValue(key, out var placeholder))
                    {
                        string? value = null;

                        if (!placeholder.IsOptional && string.IsNullOrEmpty(valuesItem.Value?.ToString()))
                        {
                            HandleException(new InvalidOperationException($"Value for '{valuesItem.Key}' is required"), ref error);
                        }

                        switch (placeholder.Component)
                        {
                            case ChildDictionaryComponent childDictionaryComponent:
                                object? parentKeyValue = default;

                                if (!valuesClone.TryGetValue(TryResolveAlias($"$aliases.components.['{childDictionaryComponent.Parent.ToLowerInvariant()}']", out var parentKey) ? parentKey! : string.Empty, out parentKeyValue) || string.IsNullOrEmpty(parentKeyValue?.ToString()))
                                {
                                    HandleException(new InvalidOperationException($"Invalid Parent key '{childDictionaryComponent.Parent}' for '{valuesItem.Key}'"), ref error);
                                }

                                IDictionary<string, string>? childDictionary = default;

                                isValidValue = childDictionaryComponent.Source.TryGetValue(parentKeyValue?.ToString()?.ToLowerInvariant() ?? string.Empty, out childDictionary) && (childDictionary?.TryGetValue(valuesItem.Value?.ToString()?.ToLowerInvariant() ?? string.Empty, out value) ?? false);
                                
                                if (!isValidValue)
                                {
                                    HandleException(new InvalidOperationException($"Value '{valuesItem.Value}' for '{valuesItem.Key}' is not found"), ref error);
                                }
                                break;
                            case DictionaryComponent dictionaryComponent:
                                isValidValue = dictionaryComponent.Source.TryGetValue(valuesItem.Value?.ToString()?.ToLowerInvariant() ?? string.Empty, out value);

                                if (!isValidValue)
                                {
                                    HandleException(new InvalidOperationException($"Value '{valuesItem.Value}' for '{valuesItem.Key}' is not found"), ref error);
                                }
                                break;
                            case InstanceComponent instanceComponent:
                                isValidValue = int.TryParse(valuesItem.Value?.ToString(), out var intValue);

                                if (isValidValue)
                                {
                                    isValidValue = instanceComponent.MinValue is not null && intValue >= instanceComponent.MinValue || intValue <= instanceComponent.MaxValue;
                                    
                                    if (isValidValue)
                                    {
                                        if (instanceComponent.Padding is not null)
                                        {
                                            value = valuesItem.Value?.ToString()?.PadLeft(instanceComponent.Padding.TotalLength, instanceComponent.Padding.Character);
                                        }
                                        else
                                        {
                                            value = intValue.ToString();
                                        }
                                    }
                                    else
                                    {
                                        HandleException(new InvalidOperationException($"Value '{valuesItem.Value}' for '{valuesItem.Key}' is out of range"), ref error);
                                    }
                                }
                                else
                                {
                                    HandleException(new InvalidOperationException($"Value '{valuesItem.Value}' for '{valuesItem.Key}' is not a valid integer"), ref error);
                                }
                                break;
                            case FreeTextComponent freeTextComponent:
                                value = valuesItem.Value?.ToString();
                                isValidValue = true;
                                break;
                            case UniqueComponent uniqueComponent:
                                var seed = valuesItem.Value?.ToString() ?? uniqueComponent.Seed;
                                isValidValue = bool.TryParse(seed, out var boolValue);

                                if (isValidValue && boolValue)
                                {
                                    var stringBuilder = new StringBuilder();

                                    foreach (var orderedValuesItem in placeholders.ToDictionary(item => item.Key, item => valuesClone.TryGetValue(item.Key, out var value) ? value : string.Empty).Where(item => item.Value is not null).OrderBy(item => item.Key).Select(item => item.Value).ToArray())
                                    {
                                        stringBuilder.Append(orderedValuesItem);
                                    }

                                    seed = stringBuilder.ToString();
                                }
                                else if (!isValidValue)
                                {
                                    isValidValue = !string.IsNullOrEmpty(seed);
                                }

                                if (isValidValue)
                                {
                                    value = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(seed!))).ToString("N")[..uniqueComponent.Length];
                                }
                                else
                                {
                                    HandleException(new InvalidOperationException($"Seed value for '{valuesItem.Key}' is not valid"), ref error);
                                }
                                break;
                        }

                        switch (placeholder.Component.Casing)
                        {
                            case Casing.Lower:
                                value = value?.ToLowerInvariant();
                                break;
                            case Casing.Upper:
                                value = value?.ToUpperInvariant();
                                break;
                        }

                        result[key] = isValidValue ? value : $"{{{valuesItem.Key}}}";
                    }
                }
            }

            var templateKeyValue = templateConfig.Key?.ToLowerInvariant();

            if (!string.IsNullOrEmpty(templateKeyValue))
            {
                if (_jsonService.TryGetValue("$.templateComponentKey", out var templateComponentKey, SourceGenerationContext.Default.String) && !string.IsNullOrEmpty(templateComponentKey))
                {
                    templateComponentKey = templateComponentKey.ToLowerInvariant();
                    var templateKeyReferences = (_componentAliasReferences.Value.TryGetValue(templateComponentKey, out var templateKeyReference) ? templateKeyReference : []).Append(templateComponentKey).ToArray();
                    
                    foreach (var templateKeyReferencesItem in templateKeyReferences)
                    {
                        if (result.TryGetValue(templateKeyReferencesItem, out var templateKey) && !string.IsNullOrEmpty(templateKey))
                        {
                            templateKeyValue = templateKey;

                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(templateKeyValue))
                    {
                        var keyComponent = GetComponent<DictionaryComponent>(templateComponentKey, ref error);

                        if (keyComponent?.Source.TryGetValue(templateKeyValue, out var templateKeyValueValue) ?? false)
                        {
                            foreach (var templateKeyReferencesItem in templateKeyReferences)
                            {
                                if (placeholders.ContainsKey(templateKeyReferencesItem))
                                {
                                    result[templateKeyReferencesItem] = templateKeyValueValue;
                                    valuesClone[templateKeyReferencesItem] = templateKeyValue;
                                }
                            }
                        }
                    }
                }
            }

            _ResolveValues(templateConfig.Values);
            _ResolveValues(values);

            return result;
        }

        TemplateResult ReplacePlaceholders(string? template, IDictionary<string, string?> values)
        {
            string ReplacePlaceholder(string expression, string placeholder, string ?value)
            {
                return expression.Replace($"{{{placeholder}}}", value, StringComparison.OrdinalIgnoreCase);
            }

            var result = template ?? string.Empty;
            var success = true;

            foreach (var optionalItem in OptionalPlaceholderRegex().Matches(template ?? string.Empty).ToArray())
            {
                var optionalPart = optionalItem.Groups[1].Value;

                foreach (var optionalPlaceholderMatch in PlaceholderRegex().Matches(optionalPart).ToArray())
                {
                    var placeholder = optionalPlaceholderMatch.Groups[1].Value;
                    var placeholderValueKey = ResolveComponentAlias(placeholder).ToLowerInvariant();

                    if (values.TryGetValue(placeholderValueKey, out string? value))
                    {
                        optionalPart = ReplacePlaceholder(optionalPart, placeholder, value);
                    }
                    else
                    {
                        optionalPart = null;

                        break;
                    }
                }

                result = result.Replace(optionalItem.Groups[0].Value, optionalPart);
            }

            foreach (var placeholderMatch in PlaceholderRegex().Matches(result).ToArray())
            {
                var placeholder = placeholderMatch.Groups[1].Value;
                var placeholderValueKey = ResolveComponentAlias(placeholder).ToLowerInvariant();

                if (values.TryGetValue(placeholderValueKey, out string? value))
                {
                    result = ReplacePlaceholder(result, placeholder, value);
                }
                else
                {
                    HandleException(new InvalidOperationException($"Value for '{placeholder}' is required"), ref error);

                    success = false;
                }
            }

            foreach (var keyItem in values.Keys)
            {
                if (result.Contains($"{{{keyItem.ToLowerInvariant()}}}"))
                {
                    HandleException(new InvalidOperationException($"Placeholder '{keyItem}' is not used"), ref error);

                    success = false;
                }
            }

            return new TemplateResult(template ?? string.Empty, result, success);
        }

        string ApplyCasing(string value, Casing casing)
        {
            return casing switch
            {
                Casing.Lower => value.ToLowerInvariant(),
                Casing.Upper => value.ToUpperInvariant(),
                _ => value
            };
        }

        string? FormatNameValue(string name, string? value)
        {
            return !string.IsNullOrEmpty(value) ? $"{name}: {value}" : null;
        }

        var templateConfig = GetTemplateConfig(keyOrTemplate, ref error);
        var placeholders = GetPlaceholders(templateConfig!.Template ?? string.Empty);
        var resolvedValues = ResolveValues(templateConfig, placeholders, values);
        var valuesInfo = new ValuesInfo()
            {
                Values = resolvedValues,
                ValidValuesKeys = [.. values.Keys.Where(key => placeholders.ContainsKey(key.ToLowerInvariant()))],
                InvalidValuesKeys = [.. placeholders.Where(item => !item.Value.IsOptional).Select(item => item.Value.Component.Name).Where(name => !values.Keys.Select(key => key.ToLowerInvariant()).Contains(name.ToLowerInvariant()) && !resolvedValues.ContainsKey(name.ToLowerInvariant()))],
                AdditionalValuesKeys = [.. values.Keys.Where(key => !placeholders.ContainsKey(key.ToLowerInvariant()))]
            };

        if (NoAdditionalValues && valuesInfo.AdditionalValuesKeys.Length > 0)
        {
            HandleException(new InvalidOperationException($"Additional values not allowed. Additional values found: {string.Join(", ", valuesInfo.AdditionalValuesKeys)}"), ref error);
        }

        foreach (var placeholdersItem in placeholders)
        {
            var isValidValue = false;

            if (resolvedValues.TryGetValue(placeholdersItem.Key, out var value))
            {
                if (placeholdersItem.Value.IsOptional || !string.IsNullOrEmpty(value))
                {
                    isValidValue = true;
                }
            }
            else if (placeholdersItem.Value.IsOptional)
            {
                isValidValue = true;
            }

            if (!isValidValue)
            {
                HandleException(new InvalidOperationException($"Value for '{placeholdersItem.Value.Name}' is required"), ref error);
            }
        }

        var templateResult = ReplacePlaceholders(templateConfig.Template, resolvedValues);
        var fullResult = templateResult.Result;
        var result = fullResult;

        if (templateResult.Success)
        {
            if (string.IsNullOrEmpty(templateConfig.Key))
            {
                if (_jsonService.TryGetValue("$.templateComponentKey", out var templateComponentKey, SourceGenerationContext.Default.String) && !string.IsNullOrEmpty(templateComponentKey))
                {
                    templateComponentKey = ResolveComponentAlias(templateComponentKey);

                    if (resolvedValues.TryGetValue(templateComponentKey.ToLowerInvariant(), out var keyComponentValue) && !string.IsNullOrEmpty(keyComponentValue))
                    {
                        var keyTemplateConfig = GetTemplateConfig(keyComponentValue, ref error);
                        var template = new {
                            templateConfig.Key,
                            templateConfig.Template,
                        };
                        templateConfig = keyTemplateConfig;
                        templateConfig.Key = template.Key;
                        templateConfig.Template = template.Template;
                    }
                }
            }

            templateResult = new TemplateResult(templateResult.Template, ApplyCasing(templateResult.Result, templateConfig.Casing ?? Casing.None), templateResult.Success);
            fullResult = templateResult.Result;
            result = fullResult;
            
            if (templateConfig.LengthMax is not null && fullResult.Length > templateConfig.LengthMax)
            {
                if (AllowTruncation)
                {
                    result = fullResult.Length >= templateConfig.LengthMax
                        ? fullResult[..templateConfig.LengthMax.Value]
                        : fullResult;

                    foreach (var invalidCharactersItem in templateConfig.InvalidCharactersEnd?.ToCharArray() ?? [])
                    {
                        result = result.TrimEnd(invalidCharactersItem);
                    }
                }
                else
                {
                    HandleException(new InvalidOperationException($"Max length exceeded for '{fullResult}'. Max length: {templateConfig.LengthMax}, Result length: {fullResult.Length}"), ref error);
                }
            }

            if (string.IsNullOrEmpty(templateConfig.StaticValue) && !string.IsNullOrEmpty(templateConfig.Regex) && !Regex.IsMatch(result, templateConfig.Regex))
            {
                var message = $"Result '{result}' does not match regex '{templateConfig.Regex}'";

                foreach (var messageItem in new string?[]
                {
                    FormatNameValue("TemplateKey", keyOrTemplate),
                    FormatNameValue(nameof(TemplateConfig.Template), templateConfig.Template),
                    FormatNameValue(nameof(TemplateConfig.ValidText), templateConfig.ValidText),
                    FormatNameValue(nameof(TemplateConfig.InvalidText), templateConfig.InvalidText)
                })
                {
                    message = !string.IsNullOrEmpty(messageItem) ? $"{message}{Environment.NewLine}{messageItem}" : message;
                }

                HandleException(new InvalidOperationException(message), ref error);

                templateResult = new TemplateResult(templateResult.Template, result, false);
            }

            if (templateResult.Success)
            {
                if (CheckUniqueName)
                {
                    if (string.IsNullOrEmpty(SubscriptionId))
                    {
                        HandleException(new InvalidOperationException("SubscriptionId is required for unique name check"), ref error);
                    }

                    if (!CheckNameAvailability(result, templateConfig.Name, SubscriptionId!, out error, ResourceGroupName, Location))
                    {
                        HandleException(new InvalidOperationException(error), ref error);
                    }
                }
            }
        }

        return new NameInfo(
            new TemplateInfo()
            {
                Key = !string.IsNullOrEmpty(templateConfig.Key) ? keyOrTemplate : null,
                ActualKey = templateConfig.Key,
                Template = templateConfig.Template!,
                Casing = templateConfig.Casing ?? Casing.None,
                LengthMax = templateConfig.LengthMax,
                LengthMin = templateConfig.LengthMin,
                Placeholders = [.. placeholders.Values],
                AllowTruncation = AllowTruncation,
                InvalidCharacters = templateConfig.InvalidCharacters,
                InvalidCharactersConsecutive = templateConfig.InvalidCharactersConsecutive,
                InvalidCharactersEnd = templateConfig.InvalidCharactersEnd,
                InvalidCharactersStart = templateConfig.InvalidCharactersStart,
                InvalidText = templateConfig.InvalidText,
                ValidText = templateConfig.ValidText,
                Regex = templateConfig.Regex,
                StaticValue = templateConfig.StaticValue
            },
            valuesInfo,
            result,
            fullResult,
            NoAdditionalValues,
            error
        );
    }

    private T? GetComponent<T>(string name, ref string? error) where T : BaseComponent
    {
        return GetComponent(name, ref error) as T;
    }

    private BaseComponent? GetComponent(string name, ref string? error)
    {
        JsonNode? ResolveNodeValue(JsonNode? node)
        {
            if (node is not null)
            {
                var valueKind = node.GetValueKind();
                JsonNode? valueNode = default;

                valueNode = valueKind switch
                {
                    JsonValueKind.Object => node.ToSelectable()["value"].Node,
                    _ => node,
                };

                if (valueNode is not null)
                {
                    return valueNode;
                }

                return node;
            }

            return default;
        }

        JsonNode? ResolveProperties(JsonObject? objectNode)
        {
            if (objectNode is not null)
            {
                var propertiesToUpdate = new Dictionary<string, JsonNode>();

                foreach (var property in objectNode)
                {
                    if (property.Value is not null)
                    {
                        propertiesToUpdate[property.Key.ToLowerInvariant()] = ResolveNodeValue(property.Value)!.DeepClone();
                    }
                }

                foreach (var property in propertiesToUpdate)
                {
                    objectNode[property.Key] = property.Value;
                }

                return objectNode.DeepClone();
            }

            return default;
        }

        if (string.IsNullOrEmpty(name))
        {
            HandleException(new InvalidOperationException("Component name is required"), ref error);
        }

        name = ResolveComponentAlias(name);
        var nameLower = name.ToLowerInvariant();

        if (_componentCache.TryGetValue(nameLower, out var cachedComponent))
        {
            return cachedComponent;
        }

        if (_jsonService.TryGetNode($"$.components.{nameLower}", out var node, out var propertyName))
        {
            var objectNode = node!.AsObject();
            var componentType = Enum.Parse<ComponentType>(objectNode["type"]!.GetValue<string>(), true);
            BaseComponent? result = null;
            JsonObject? sourceNode = null;
            var propertiesPropetyName = "properties";

            if (objectNode.ContainsKey(propertiesPropetyName))
            {
                objectNode = objectNode[propertiesPropetyName]!.AsObject();
            }

            objectNode["name"] = propertyName;
            objectNode["type"] = componentType.ToString();

            switch (componentType)
            {
                case ComponentType.ChildDictionary:
                    var childDictionaryProperty = new {
                        Source = nameof(ChildDictionaryComponent.Source)
                    };
                    sourceNode = objectNode[childDictionaryProperty.Source]?.AsObject();

                    if (sourceNode is null || sourceNode.Count == 0)
                    {
                        HandleException(new InvalidOperationException($"{childDictionaryProperty.Source} is required for child dictionary component"), ref error);
                    }

                    var propertiesToUpdate = new Dictionary<string, JsonNode?>();

                    foreach (var sourceItem in sourceNode!)
                    {
                        var sourceItemValue = ResolveNodeValue(sourceItem.Value)?.AsObject();

                        if (sourceItemValue is null || sourceItemValue.Count == 0)
                        {
                            HandleException(new InvalidOperationException($"{childDictionaryProperty.Source} item is required for child dictionary component '{propertyName}'"), ref error);
                        }

                        propertiesToUpdate[sourceItem.Key.ToLowerInvariant()] = ResolveProperties(sourceItemValue);
                    }

                    foreach (var property in propertiesToUpdate)
                    {
                        sourceNode[property.Key] = property.Value;
                    }

                    result = _jsonService.DeserializeNode(objectNode, SourceGenerationContext.Default.ChildDictionaryComponent);

                    var childDictionaryComponent = (ChildDictionaryComponent)result!;
                    childDictionaryComponent.Source = childDictionaryComponent.Source.ToDictionary(item => item.Key.ToLowerInvariant(), item => (IDictionary<string, string>)item.Value.ToDictionary(item => item.Key.ToLowerInvariant(), item => item.Value));
                    break;
                case ComponentType.Dictionary:
                    var dictionaryProperty = new {
                        Source = nameof(DictionaryComponent.Source)
                    };
                    sourceNode = objectNode[dictionaryProperty.Source]?.AsObject();

                    if (sourceNode is null || sourceNode.Count == 0)
                    {
                        HandleException(new InvalidOperationException($"{dictionaryProperty.Source} is required for dictionary component '{propertyName}'"), ref error);
                    }

                    objectNode[dictionaryProperty.Source] = ResolveProperties(sourceNode);

                    result = _jsonService.DeserializeNode(objectNode, SourceGenerationContext.Default.DictionaryComponent);

                    var dictionaryComponent = (DictionaryComponent)result!;
                    dictionaryComponent.Source = dictionaryComponent.Source.ToDictionary(item => item.Key.ToLowerInvariant(), item => item.Value);
                    break;
                case ComponentType.Instance:
                    result = _jsonService.DeserializeNode(objectNode, SourceGenerationContext.Default.InstanceComponent);
                    break;
                case ComponentType.FreeText:
                    result = _jsonService.DeserializeNode(objectNode, SourceGenerationContext.Default.FreeTextComponent);
                    break;
                case ComponentType.Unique:
                    var uniqueProperty = new {
                        Length = nameof(UniqueComponent.Length)
                    };
                    var lengthNode = objectNode.ToSelectable()[uniqueProperty.Length].Node;

                    if (lengthNode is not null)
                    {
                        var length = lengthNode.GetValue<int>();

                        if (length < UniqueComponent.LengthMin)
                        {
                            HandleException(new InvalidOperationException($"{uniqueProperty.Length} must be greater than {UniqueComponent.LengthMin - 1}"), ref error);
                        }

                        if (length > UniqueComponent.LengthMax)
                        {
                            HandleException(new InvalidOperationException($"{uniqueProperty.Length} must be less than {UniqueComponent.LengthMax + 1}"), ref error);
                        }
                    }
                    else
                    {
                        objectNode[uniqueProperty.Length] = UniqueComponent.LengthDefault;
                    }

                    result = _jsonService.DeserializeNode(objectNode, SourceGenerationContext.Default.UniqueComponent);
                    break;
            }

            _componentCache.AddOrUpdate(nameLower, result!, (_, _) => result!);

            return result;
        }

        HandleException(new InvalidOperationException($"Component '{name}' not found"), ref error);

        return default;
    }

    private TemplateConfig GetTemplateConfig(string keyOrTemplate, ref string? error)
    {
        bool TryGetTemplateNode(string key, out JsonNode? node, out string? propertyName)
        {
            var formatString = "$.templates.['{0}']";

            return _jsonService.TryGetNode(string.Format(formatString, key), out node, out propertyName);
        }

        if (string.IsNullOrEmpty(keyOrTemplate))
        {
            HandleException(new InvalidOperationException("Template key or value is required"), ref error);
        }

        keyOrTemplate = ResolveTemplateAlias(keyOrTemplate);

        if (_templateCache.TryGetValue(keyOrTemplate.ToLowerInvariant(), out var cachedTemplate))
        {
            return cachedTemplate;
        }

        var regexTemplateString = @"^{0}.*{0}$";
        TemplateConfig result;

        if (Regex.IsMatch(
            keyOrTemplate, string.Format(regexTemplateString, "'"))
            || Regex.IsMatch(keyOrTemplate, string.Format(regexTemplateString, '"'))
            || (
                keyOrTemplate.Contains('{')
                && keyOrTemplate.Contains('}')
            )
        )
        {
            result = new TemplateConfig()
            {
                Template = keyOrTemplate.Trim('\'' , '"')
            };
        }
        else
        {
            if (TryGetTemplateNode(keyOrTemplate, out var node, out var propertyName))
            {
                var objectNode = node!.AsObject();
                var propertiesPropertyName = "properties";
                var propertiesNode = objectNode[propertiesPropertyName]!.AsObject();
                var defaultNode = new Lazy<JsonObject?>(() => TryGetTemplateNode("default", out var node, out _) ? node!.AsObject()[propertiesPropertyName]!.AsObject() : null);
                var optionalProperties = new string[]
                {
                    nameof(TemplateConfig.Values)
                };

                foreach (var property in new string[] {
                    nameof(TemplateConfig.Casing),
                    nameof(TemplateConfig.Template),
                    nameof(TemplateConfig.LengthMax),
                    nameof(TemplateConfig.LengthMin),
                    nameof(TemplateConfig.InvalidCharacters),
                    nameof(TemplateConfig.InvalidCharactersConsecutive),
                    nameof(TemplateConfig.InvalidCharactersEnd),
                    nameof(TemplateConfig.InvalidCharactersStart),
                    nameof(TemplateConfig.InvalidText),
                    nameof(TemplateConfig.ValidText),
                    nameof(TemplateConfig.Regex),
                    nameof(TemplateConfig.StaticValue),
                    nameof(TemplateConfig.Values)
                })
                {
                    if (!propertiesNode.ContainsKey(property) && defaultNode.Value is not null)
                    {
                        var isInvalidDefaultValue = !optionalProperties.Contains(property);

                        if (defaultNode.Value.ContainsKey(property))
                        {
                            var defaultProperty = defaultNode.Value[property];
                            var defaultValueKind = defaultProperty!.GetValueKind();

                            if (defaultValueKind is not JsonValueKind.Null && defaultValueKind is not JsonValueKind.Undefined)
                            {
                                propertiesNode[property] = defaultProperty!.DeepClone();
                                isInvalidDefaultValue = false;
                            }
                        }

                        if (isInvalidDefaultValue)
                        {
                            HandleException(new InvalidOperationException($"Invalid default value for property '{property}'"), ref error);
                        }
                    }
                }

                result = _jsonService.DeserializeNode(propertiesNode, SourceGenerationContext.Default.TemplateConfig)!;
                result!.Key = propertyName;

                _templateCache.AddOrUpdate(keyOrTemplate!.ToLowerInvariant(), result!, (_, _) => result!);
            }
            else
            {
                result = new TemplateConfig()
                {
                    Template = keyOrTemplate
                };
            }
        }

        return result;
    }

    private bool CheckNameAvailability(string name, string? resourceType, string subscriptionId, out string? error, string? resourceGroupName = default, string? location = default)
    {
        error = default;
        var service = resourceType?.ToLowerInvariant();
        var propertyName = new
        {
            ApiVersion = "apiVersion",
            CheckName = "checkName",
            Exist = "exist",
            Uri = "uri",
            Body = "body",
            Requests = "requests",
            Properties = "properties",
            PurgeProtectionEnabled = "purgeProtectionEnabled",
            VaultId = "vaultId"
        };
        var azureUri = new
        {
            KeyVault = new
            {
                DeletedVaultsFormat = "https://management.azure.com/subscriptions/{0}/providers/Microsoft.KeyVault/locations/{1}/deletedVaults/{2}?api-version={3}"
            }
        };

        if (string.IsNullOrEmpty(service))
        {
            return true;
        }

        if (!_jsonService.TryGetNode($"$.restApis.{service}", out var node))
        {
            return true;
        }

        var requestNode = node!.AsObject()[propertyName.Properties];

        if (!string.IsNullOrEmpty(resourceGroupName))
        {
            var existResponse = _azureRestService.InvokeAsync(httpClient => httpClient.GetAsync(
                string.Format(
                    requestNode![propertyName.Requests]![propertyName.Exist]![propertyName.Uri]!.GetValue<string>(),
                    subscriptionId,
                    resourceGroupName,
                    name
                )
            ))
            .GetAwaiter()
            .GetResult();

            if (existResponse.StatusCode == HttpStatusCode.OK)
            {
                return true;
            }

            if (existResponse.StatusCode != HttpStatusCode.NotFound)
            {
                HandleException(new Exception($"Error in CheckNameAvailability for resource type '{resourceType}' with name '{name}'", new HttpRequestException(existResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())), ref error);

                return false;
            }

            switch (resourceType)
            {
                case "KeyVault/vaults":
                    if (!string.IsNullOrEmpty(location))
                    {
                        var deletedKeyVaultResponse = _azureRestService.InvokeAsync(httpClient => httpClient.GetAsync(
                            string.Format(
                                azureUri.KeyVault.DeletedVaultsFormat,
                                subscriptionId,
                                location,
                                name,
                                requestNode![propertyName.ApiVersion]!
                            )
                        ))
                        .GetAwaiter()
                        .GetResult();

                        if (deletedKeyVaultResponse.StatusCode == HttpStatusCode.OK)
                        {
                            var deletedKeyVaultObject = _jsonService.Deserialize(
                                deletedKeyVaultResponse.Content
                                    .ReadAsStringAsync()
                                    .GetAwaiter()
                                    .GetResult(),
                                SourceGenerationContext.Default.JsonNode
                            )!.AsObject();

                            var propertiesObject = deletedKeyVaultObject[propertyName.Properties]!.AsObject();

                            if (
                                propertiesObject[propertyName.VaultId]!.GetValue<string>() == $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.KeyVault/vaults/{name}"
                                && (
                                    !propertiesObject.ContainsKey(propertyName.PurgeProtectionEnabled)
                                    || !propertiesObject[propertyName.PurgeProtectionEnabled]!.GetValue<bool>()
                                )
                            )
                            {
                                return true;
                            }
                        }
                    }
                    break;
            }
        }

        var body = _jsonService.Serialize(requestNode![propertyName.Requests]![propertyName.CheckName]![propertyName.Body]!, SourceGenerationContext.Default.JsonNode);
        body = body.Replace("{NAME}", name);

        JsonObject checkNameObject;

        try {
            var checkNameContent = _azureRestService.PostAsync(string.Format(requestNode[propertyName.Requests]![propertyName.CheckName]![propertyName.Uri]!.GetValue<string>(), subscriptionId), body)
                .GetAwaiter()
                .GetResult().Content
                .ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();

            if (string.IsNullOrEmpty(checkNameContent))
            {
                return true;
            }

            checkNameObject = _jsonService.Deserialize(checkNameContent, SourceGenerationContext.Default.JsonNode)!.AsObject();
        }
        catch (HttpRequestException httpRequestException)
        {
            HandleException(new Exception($"Error in CheckNameAvailability for resource type '{resourceType}' with name '{name}'", httpRequestException), ref error);

            return false;
        }

        var result = (checkNameObject["nameAvailable"] ?? checkNameObject["isAvailiable"])!.GetValue<bool>();

        if (!result)
        {
            error = checkNameObject["message"]!.GetValue<string>();
        }

        return result;
    }

    private void HandleException(Exception exception, ref string? error)
    {
        error ??= exception.Message;

        if (!SuppressError)
        {
            throw exception;
        }
    }
}