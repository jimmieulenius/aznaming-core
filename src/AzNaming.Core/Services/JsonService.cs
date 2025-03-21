using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using AzNaming.Core.Extensions;
using AzNaming.Core.Models;

namespace AzNaming.Core.Services;

public partial class JsonService(FileService fileService)
{
    private class PathConstants{
        public const string EnclosureBracket = "[";
        public const string EnclosureSingleQuote = "'";
        public const string EnclosureDoubleQuote = "\"";
        public const string SeparatorDot = ".";
        public const string IdHashSign = "#";
    }

    private readonly FileService _fileService = fileService;

    private readonly List<JsonSection> _sections = [];

    private static readonly Assembly _entryAssembly = Assembly.GetEntryAssembly()!;

    private static readonly string _entryAssemblyName = _entryAssembly.GetName().Name!;

    private readonly string _entryAssemblyDirectory = Path.GetDirectoryName(AppContext.BaseDirectory)!;

    [GeneratedRegex(@"\[(.*?)\]")]
    private static partial Regex EnclosureBracket();

    [GeneratedRegex(@"'(.*?)'")]
    private static partial Regex EnclosureSingleQuote();

    [GeneratedRegex(@"""(.*?)""")]
    private static partial Regex EnclosureDoubleQuote();

    public string Serialize<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        return JsonSerializer.Serialize(value, jsonTypeInfo);
    }

    public T? Deserialize<T>(string json, JsonTypeInfo<T> jsonTypeInfo)
    {
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }

    public T? DeserializeFile<T>(string file, JsonTypeInfo<T> jsonTypeInfo)
    {
        return JsonSerializer.Deserialize(File.ReadAllText(file), jsonTypeInfo);
    }

    public T? DeserializeNode<T>(JsonNode node, JsonTypeInfo<T> jsonTypeInfo)
    {
        return node.Deserialize(jsonTypeInfo);
    }

    public JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            int => (int)value,
            string => (string)value,
            _ => null
        };
    }

    public void ExpandSection(string uriOrJson, Action<JsonSection> action, string? path = null)
    {
        var schemeKind = new {
            File = "file",
            Http = "http",
            Json = "json",
            Embedded = "embedded"
        };
        string scheme = schemeKind.File;

        if (Uri.TryCreate(uriOrJson, UriKind.Absolute, out var uriResult)
                && uriResult is not null
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            scheme = schemeKind.Http;
        }
        else if (!string.IsNullOrEmpty(Path.GetPathRoot(uriOrJson)))
        {
            scheme = schemeKind.File;
        }
        else if (uriOrJson.StartsWith(_entryAssemblyName))
        {
            scheme = schemeKind.Embedded;
        }
        else
        {
            scheme = schemeKind.Json;
        }

        if (scheme == schemeKind.Json)
        {
            action(new JsonSection(
                string.Empty,
                (section) => new JsonSectionResolveResult<Lazy<JsonNode>>(new Lazy<JsonNode>(() => InitializeNode(JsonNode.Parse(uriOrJson)!, path))),
                path
            ));
        }
        else
        {
            if (!ContainsSection(uriOrJson))
            {
                action(new JsonSection(uriOrJson, ResolveSection, path));
            }
        }
    }

    public void AddSection(string uriOrJson, string? path = null)
    {
        ExpandSection(uriOrJson, _sections.Add, path);
    }

    public void InsertSection(int index, string uriOrJson, string? path = null)
    {
        ExpandSection(uriOrJson, (section) => _sections.Insert(index, section), path);
    }

    public void ClearSections()
    {
        _sections.Clear();
    }

    public bool TryGetValue<T>(string path, out T? value, JsonTypeInfo<T> jsonTypeInfo)
    {
        return TryGetValue(path, out value, out _, jsonTypeInfo);
    }

    public bool TryGetValue<T>(string path, out T? value, out string? propertyName, JsonTypeInfo<T> jsonTypeInfo)
    {
        value = default;
        var result = TryGetNode(path, out var node, out propertyName);

        if (result)
        {
            if (node is not null)
            {
                value = DeserializeNode<T>(node, jsonTypeInfo);
            }
        }

        return result;
    }

    public bool TryGetNode(string path, out JsonNode? node)
    {
        return TryGetNode(path, out node, out _);
    }

    public bool TryGetNode(string path, out JsonNode? node, out string? propertyName)
    {
        var specialPropertyName = new
        {
            Id = "$id",
            Ref = "$ref"
        };
        string? originPath = default;
        propertyName = default;
        string? originPropertyName = default;

        bool _TryGetNode(string path, out JsonNode? node, out string? propertyName, string? sectionId = null, List<string>? refPathsVisited = default, JsonObject? objectOverride = null)
        {
            void AddNodeToCache(JsonNode node, string? propertyName, string path, string sectionId, List<string>? refPathsVisited = default)
            {
                void _AddNodeToCache(string key)
                {
                    var cachedNode = new CachedJsonNode
                    {
                        Node = node,
                        PropertyName = propertyName,
                        Key = key
                    };
                    var sectionNodeCache = _sections.Single(item => item.Id == sectionId).NodeCache;
                    sectionNodeCache.AddOrUpdate(key.ToLowerInvariant(), cachedNode, (_, _) => cachedNode);
                }

                node = ResolveNode(node, refPathsVisited);

                var id = node.ToSelectable()[specialPropertyName.Id].Node?.GetValue<string>();

                if (!string.IsNullOrEmpty(id))
                {
                    _AddNodeToCache(id);
                }

                _AddNodeToCache(path);
            }

            JsonNode FinalizeNode(JsonNode node, string? propertyName, string sectionId, List<string>? refPathsVisited = default, string? path = default)
            {
                var nodePath = refPathsVisited is not null ? node.GetPath() : path ?? originPath;

                AddNodeToCache(node, propertyName, nodePath, sectionId, refPathsVisited);

                node = ResolveNode(OverrideProperties(node!, objectOverride, refPathsVisited), refPathsVisited);

                return node;
            }

            bool TryGetNodeFromCache(string path, string? sectionId, out JsonNode? node, out string? propertyName)
            {
                node = default;
                propertyName = default;

                foreach (var sectionsItem in _sections)
                {
                    if (sectionId is not null && sectionsItem.Id != sectionId)
                    {
                        continue;
                    }

                    if (sectionsItem.NodeCache.TryGetValue(path.ToLowerInvariant(), out var cachedNode))
                    {
                        node = cachedNode.Node;
                        propertyName = cachedNode.PropertyName;

                        return true;
                    }
                }

                return false;
            }

            node = default;
            propertyName = default;

            if (TryGetNodeFromCache(path, sectionId, out node, out propertyName))
            {
                return true;
            }

            for (var sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
            {
                var sectionsItem = _sections[sectionIndex];

                if (sectionId is not null && sectionsItem.Id != sectionId)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(sectionsItem.Path) && !path.StartsWith(sectionsItem.Path, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sectionNode = sectionsItem.Result.Value.Result?.Value;

                if (sectionsItem.Result.Value.HasResult && sectionNode is not null)
                {
                    node = sectionNode.SelectNodes(path).SingleOrDefault();

                    if (node is not null)
                    {
                        originPath ??= node.GetPath();

                        if (node.Parent is not null)
                        {
                            propertyName = node.GetPropertyName();
                            originPropertyName ??= propertyName;
                        }

                        var nodePath = node.ToSelectable()[specialPropertyName.Id].Node?.GetValue<string>() ?? node.GetPath();

                        if (refPathsVisited is not null)
                        {
                            if (refPathsVisited.Contains(nodePath))
                            {
                                refPathsVisited.Add(nodePath);

                                throw new InvalidOperationException($"Circular reference detected at '{string.Join(" -> ", refPathsVisited)}'");
                            }

                            refPathsVisited.Add(nodePath);
                        }

                        var refPath = node.ToSelectable()[specialPropertyName.Ref].Node?.GetValue<string>();

                        if (!string.IsNullOrEmpty(refPath))
                        {
                            string? idSectionId = default;

                            if (TryGetId(refPath, out var id, out var remainingPath))
                            {
                                if (TryGetNodePathById(id!, out var idPath, out idSectionId))
                                {
                                    refPath = $"{idPath}{remainingPath}";
                                }
                            }

                            if (objectOverride is not null)
                            {
                                foreach (var objectNodeItem in node.AsObject())
                                {
                                    objectOverride[objectNodeItem.Key] = objectNodeItem.Value;
                                }
                            }
                            else
                            {
                                objectOverride = node.DeepClone().AsObject();
                            }

                            var result = false;

                            if (!string.IsNullOrEmpty(id))
                            {
                                if (string.IsNullOrEmpty(remainingPath))
                                {
                                    result = _TryGetNode(refPath, out node, out _, idSectionId, refPathsVisited ?? [nodePath], objectOverride);
                                }
                            }

                            if (!result)
                            {
                                result = _TryGetNode(refPath, out node, out _, idSectionId, refPathsVisited ?? [nodePath], objectOverride);
                            }

                            if (result)
                            {
                                if (!string.IsNullOrEmpty(id))
                                {
                                    node!.AsObject().Remove(specialPropertyName.Id);
                                }

                                AddNodeToCache(node!, propertyName, path, sectionsItem.Id, refPathsVisited);
                            }

                            return result;
                        }
                        else
                        {
                            node = FinalizeNode(node, propertyName, sectionsItem.Id, refPathsVisited, path);

                            if (refPathsVisited is not null && refPathsVisited.Count > 0)
                            {
                                refPathsVisited.RemoveAt(refPathsVisited.Count - 1);
                            }

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        bool TryGetNodePathById(string id, out string? path, out string? sectionId)
        {
            path = default;
            sectionId = default;

            for (var sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
            {
                var sectionsItem = _sections[sectionIndex];

                if (sectionsItem.IdPaths.Value.TryGetValue(id, out path))
                {
                    sectionId = sectionsItem.Id;

                    return true;
                }
            }

            return false;
        }

        JsonNode ResolveNode(JsonNode node, List<string>? refPathsVisited)
        {
            ArgumentNullException.ThrowIfNull(node, nameof(node));

            var valueKind = node.GetValueKind();

            switch (valueKind)
            {
                case JsonValueKind.Object:
                    var objectNode = node.AsObject();
                    var refPath = objectNode[specialPropertyName.Ref]?.GetValue<string>();
                    string? sectionId = default;

                    if (TryGetId(refPath, out var id, out var remainingPath))
                    {
                        if (TryGetNodePathById(id!.ToLowerInvariant(), out var idPath, out sectionId))
                        {
                            refPath = $"{idPath}{remainingPath}";
                        }
                    }

                    if (!string.IsNullOrEmpty(refPath))
                    {
                        if (_TryGetNode(refPath, out var refNode, out _, sectionId, refPathsVisited, node.DeepClone().AsObject()))
                        {
                            if (refNode is not null)
                            {
                                if (!string.IsNullOrEmpty(id))
                                {
                                    if (refNode is JsonObject refObjectNode)
                                    {
                                        refObjectNode.Remove(specialPropertyName.Id);
                                    }
                                }

                                return refNode;
                            }
                            
                            throw new InvalidOperationException($"Cannot resolve reference '{refPath}'");
                        }
                    }

                    var nodesToUpdate = new Dictionary<string, JsonNode>();

                    foreach (var property in objectNode)
                    {
                        if (property.Value is not null)
                        {
                            nodesToUpdate.Add(property.Key, ResolveNode(property.Value, refPathsVisited).DeepClone());
                        }
                    }

                    foreach (var nodesToUpdateItem in nodesToUpdate)
                    {
                        objectNode[nodesToUpdateItem.Key] = nodesToUpdateItem.Value;
                    }
                    break;
                case JsonValueKind.Array:
                    var arrayNode = node.AsArray();

                    for (var index = 0; index < arrayNode.Count; index++)
                    {
                        var arrayItem = arrayNode[index];

                        if (arrayItem is not null)
                        {
                            arrayNode[index] = ResolveNode(arrayItem, refPathsVisited).DeepClone();
                        }
                    }
                    break;
            }

            return node;
        }

        JsonNode OverrideProperties(JsonNode node, JsonObject? objectOverride, List<string>? refPathsVisited)
        {
            var result = node.DeepClone();

            if (result.GetValueKind() is JsonValueKind.Object && objectOverride is not null)
            {
                var objectNode = result.AsObject();
                var skipKeys = new string[]
                {
                    specialPropertyName.Id,
                    specialPropertyName.Ref
                };

                foreach (var objectOverrideItem in objectOverride)
                {
                    if (!skipKeys.Contains(objectOverrideItem.Key))
                    {
                        objectNode[objectOverrideItem.Key] = objectOverrideItem.Value?.DeepClone();
                    }
                }
            }

            return result;
        }

        bool TryGetId(string? path, out string? id, out string? remainingPath)
        {
            id = null;
            remainingPath = path;

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (path.StartsWith(PathConstants.IdHashSign))
            {
                path = path[1..];

                if (path.StartsWith(PathConstants.SeparatorDot))
                {
                    path = path[1..];
                }

                var regex = new
                {
                    EnclosureBracket = EnclosureBracket(),
                    EnclosureSingleQuote = EnclosureSingleQuote(),
                    EnclosureDoubleQuote = EnclosureDoubleQuote()
                };

                if (path.StartsWith(PathConstants.EnclosureBracket))
                {
                    var match = regex.EnclosureBracket.Match(path);

                    if (match.Success)
                    {
                        remainingPath = path[(match.Index + match.Length)..];
                        id = match.Groups[1].Value;

                        foreach (var regexItem in new Regex[]
                        {
                            regex.EnclosureSingleQuote,
                            regex.EnclosureDoubleQuote,
                        })
                        {
                            match = regexItem.Match(id);

                            if (match.Success)
                            {
                                id = match.Groups[1].Value;

                                return true;
                            }
                        }

                        return true;
                    }
                }

                id = path?.ToLowerInvariant();
                remainingPath = null;

                return !string.IsNullOrEmpty(id);
            }

            return false;
        }

        return _TryGetNode(path, out node, out propertyName);
    }

    public T? Deserialize<T>(string uri, JsonTypeInfo<T> jsonTypeInfo, string? baseUri = default)
    {
        var filePath = uri;

        if (filePath.StartsWith(_entryAssemblyName))
        {
            using var streamReader = new StreamReader(_entryAssembly.GetManifestResourceStream(filePath)!);

            return Deserialize(streamReader.ReadToEnd(), jsonTypeInfo)!;
        }
        else
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var uriResult)
                && uriResult is not null
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                filePath = _fileService.DownloadFileAsync(uriResult).GetAwaiter().GetResult();
            }
            else
            {
                filePath = !string.IsNullOrEmpty(Path.GetPathRoot(filePath))
                    ? filePath
                    : Path.Combine(baseUri ?? _entryAssemblyDirectory!, filePath!);
            }

            return DeserializeFile(filePath, jsonTypeInfo)!;
        }
    }

    private  bool ContainsSection(string file)
    {
        return _sections.Any(item => item.Uri.Equals(file, StringComparison.OrdinalIgnoreCase));
    }

    private JsonSectionResolveResult<Lazy<JsonNode>> ResolveSection(JsonSection section)
    {
        var sectionIndex = _sections.IndexOf(section);
        var visitedUris = new List<string>();

        void AddSection(string uri, string? path = null, string? baseUri = default)
        {
            if (visitedUris.Contains(uri.ToLowerInvariant()))
            {
                throw new InvalidOperationException($"Circular reference detected in '{uri}'");
            }

            var propertyName = new
            {
                Sections = "sections",
                Uri = "uri",
                Path = "path"
            };
            
            JsonNode? node = default;
            var filePath = uri;

            node = Deserialize(filePath, SourceGenerationContext.Default.JsonNode, baseUri);

            visitedUris.Add(uri.ToLowerInvariant());

            var sectionsNode = node?.ToSelectable()[propertyName.Sections].Node;

            if (sectionsNode is not null && sectionsNode.GetValueKind() is JsonValueKind.Array)
            {
                var basePath = Path.GetDirectoryName(filePath);

                foreach (var sectionsNodeItem in sectionsNode.AsArray().Reverse().ToArray() ?? [])
                {
                    AddSection(sectionsNodeItem?[propertyName.Uri]?.GetValue<string>()!, sectionsNodeItem?[propertyName.Path]?.GetValue<string>(), basePath);
                }
            }
            else
            {
                var newSection = new JsonSection(
                    filePath,
                    (newSection) => new JsonSectionResolveResult<Lazy<JsonNode>>(new Lazy<JsonNode>(() => InitializeNode(node!, path))),
                    path
                );

                if (sectionIndex == _sections.Count - 1)
                {
                    _sections.Add(newSection);
                }
                else
                {
                    _sections.Insert(sectionIndex + 1, newSection);
                }
            }
        }

        AddSection(section.Uri, section.Path);

        return new JsonSectionResolveResult<Lazy<JsonNode>>(null);
    }

    private JsonNode InitializeNode(JsonNode node, string? path = default)
    {
        var result = node;
        var pathItems = path?.Split('.', StringSplitOptions.RemoveEmptyEntries) ?? [];

        if (pathItems.Length > 1)
        {
            result = result!.Nest([.. pathItems.Skip(1)]);
        }

        return result!;
    }
}