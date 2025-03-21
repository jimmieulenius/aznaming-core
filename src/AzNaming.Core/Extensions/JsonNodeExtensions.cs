using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AzNaming.Core.Extensions;

public static partial class JsonNodeExtensions
{
    #region Classes

    private static class JsonPathConstants
    {
        public const string ChildBracket = "[";
        public const string EnclosureSingleQuote = "'";
        public const string EnclosureDoubleQuote = "\"";
        public const string ChildDot = ".";
        public const string Descendants = "..";
        public const string Wildcard = "*";
        public const string Root = "$";
        public const string Union = ",";
    }

    private abstract class JsonPathSegment
    {
    }

    private class JsonPathIdSegment(string id) : JsonPathSegment
    {
        public string Id { get; private set; } = id;
    }

    private class JsonPathDescendantsSegment : JsonPathSegment
    {
    }

    private class JsonPathChildSegment(JsonPathChildSelector selector) : JsonPathSegment
    {
        public JsonPathChildSelector Selector { get; private set; } = selector;
    }

    private class JsonPathChildSegment<T>(T selector) : JsonPathChildSegment(selector) where T : JsonPathChildSelector
    {
        public new T Selector { get; private set; } = selector;
    }

    private abstract class JsonPathChildSelector
    {
    }

    private class JsonPathWildcardChildSelector : JsonPathChildSelector
    {
    }

    private class JsonPathMemberNameChildSelector(string memberName) : JsonPathChildSelector
    {
        public string MemberName { get; private set; } = memberName;
    }

    private class JsonPathArrayIndexChildSelector(int index) : JsonPathChildSelector
    {
        public int Index { get; private set; } = index;
    }

    private class JsonPathArraySliceChildSelector(int? start = 0, int? end = null, int? step = 1) : JsonPathChildSelector
    {
        public int Start { get; private set; } = start ?? 0;
        public int? End { get; private set; } = end;
        public int Step { get; private set; } = step ?? 1;
    }

    private class JsonPathUnionChildSelector(JsonPathChildSelector[] selectors) : JsonPathChildSelector
    {
        public JsonPathChildSelector[] Selectors { get; private set; } = selectors;
    }

    public struct JsonNodeSelectable(JsonNode? node, bool suppressException = true)
    {
        public static readonly JsonNodeSelectable Default = new(null, true);

        private bool _suppressException = suppressException;

        public JsonNode? Node { get; private set; } = node ?? (suppressException ? node : throw new ArgumentNullException(nameof(node)));

        public readonly bool Exist { get { return Node is not null; } }

        public readonly JsonNodeSelectable this[int index]
        {
            get
            {
                if (_suppressException)
                {
                    if (Node is null)
                    {
                        return Default;
                    }
                }

                if (!_suppressException || Node!.GetValueKind() == JsonValueKind.Array)
                {
                    var arrayNode = Node!.AsArray();

                    if (index < 0)
                    {
                        index = arrayNode.Count + index;
                    }

                    if (_suppressException)
                    {
                        if (index < 0 || index >= arrayNode.Count)
                        {
                            return Default;
                        }
                    }

                    var result = arrayNode[index];

                    if (_suppressException)
                    {
                        if (result is null)
                        {
                            return Default;
                        }
                    }

                    return new JsonNodeSelectable(result!, _suppressException);
                }
                
                return Default;
            }
        }

        public readonly JsonNodeSelectable this[string member]
        {
            get
            {
                if (_suppressException)
                {
                    if (Node is null)
                    {
                        return Default;
                    }
                }

                if (!_suppressException || Node!.GetValueKind() == JsonValueKind.Object)
                {
                    var objectNode = Node!.AsObject();

                    if (_suppressException)
                    {
                        if (!objectNode.ContainsKey(member))
                        {
                            return Default;
                        }
                    }

                    var result = objectNode[member];

                    if (_suppressException)
                    {
                        if (result is null)
                        {
                            return Default;
                        }
                    }

                    return new JsonNodeSelectable(result!, _suppressException);
                }
                
                return Default;
            }
        }
    }

    #endregion Classes

    #region Fields

    [GeneratedRegex(@"\[(.*?)\]")]
    private static partial Regex ChildBracket();

    [GeneratedRegex(@"'(.*?)'")]
    private static partial Regex EnclosureSingleQuote();

    [GeneratedRegex(@"""(.*?)""")]
    private static partial Regex EnclosureDoubleQuote();

    #endregion Fields

    #region Methods

    public static JsonNode[] GetDescendants(this JsonNode node)
    {
        var result = new List<JsonNode>();

        void GetDescendants(JsonNode? node, bool skip = false)
        {
            if (node is null)
            {
                return;
            }

            var valueKind = node.GetValueKind();

            if (valueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return;
            }

            if (!skip)
            {
                result.Add(node);
            }

            if (valueKind is JsonValueKind.Object)
            {
                foreach (var property in node.AsObject())
                {
                    GetDescendants(property.Value);
                }
            }
            else if (valueKind is JsonValueKind.Array)
            {
                foreach (var item in node.AsArray())
                {
                    GetDescendants(item);
                }
            }
        }

        GetDescendants(node);

        return [.. result];
    }

    public static JsonObject Nest(this JsonNode node, params string[] path)
    {
        ArgumentNullException.ThrowIfNull(node, nameof(node));
        
        if (path.Length == 0)
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        JsonObject? result = null;
        JsonNode childNode = node;

        foreach (var pathItem in path.Reverse().ToArray())
        {
            if (pathItem == JsonPathConstants.Root)
            {
                break;
            }

            result = new JsonObject
            {
                { pathItem, childNode }
            };
            childNode = result;
        }

        return result!;
    }

    public static JsonNode[] SelectNodes(this JsonNode? node, string path)
    {
        var result = new List<JsonNode?>();

        JsonNode[] GetResult()
        {
            return result.Where(item => item is not null).Cast<JsonNode>().ToArray();
        }

        var regex = new
        {
            ChildBracket = ChildBracket(),
            EnclosureSingleQuote = EnclosureSingleQuote(),
            EnclosureDoubleQuote = EnclosureDoubleQuote()
        };

        bool TryGetSegment(out JsonPathSegment? segment)
        {
            JsonPathChildSelector GetChildSelector(string value)
            {
                if (int.TryParse(value, out var index))
                {
                    return new JsonPathArrayIndexChildSelector(index);
                }
                else if (value == JsonPathConstants.Wildcard)
                {
                    return new JsonPathWildcardChildSelector();
                }
                else if (value.Contains(':'))
                {
                    var parts = value.Split(":");
                    int? start = null;
                    int? end = null;
                    int? step = null;

                    for (var partsIndex = 0; partsIndex < parts.Length; partsIndex++)
                    {
                        if (int.TryParse(parts[partsIndex], out var number))
                        {
                            switch (partsIndex)
                            {
                                case 0:
                                    start = number;
                                    break;
                                case 1:
                                    end = number;
                                    break;
                                case 2:
                                    step = number;
                                    break;
                            }
                        }
                    }

                    return new JsonPathArraySliceChildSelector(start, end, step);
                }

                return new JsonPathMemberNameChildSelector(value);
            }

            JsonPathSegment GetChildSegment(string value)
            {
                var selector = GetChildSelector(value);

                switch (selector)
                {
                    case JsonPathArrayIndexChildSelector arrayIndexSelector:
                        return new JsonPathChildSegment<JsonPathArrayIndexChildSelector>(arrayIndexSelector);
                    case JsonPathArraySliceChildSelector arraySliceSelector:
                        return new JsonPathChildSegment<JsonPathArraySliceChildSelector>(arraySliceSelector);
                    default:
                        return new JsonPathChildSegment<JsonPathMemberNameChildSelector>((JsonPathMemberNameChildSelector)selector);
                }
            }

            segment = null;
            string? stringValue = null;

            if (string.IsNullOrEmpty(path) || path.Length == 0)
            {
                return false;
            }
            
            if (path.StartsWith(JsonPathConstants.Root))
            {
                path = path[1..];
            }
            
            if (path.StartsWith(JsonPathConstants.Descendants))
            {
                path = path[2..];
                segment = new JsonPathDescendantsSegment();

                return true;
            }
            
            if (path.StartsWith(JsonPathConstants.ChildDot))
            {
                path = path[1..];
            }

            if (path.StartsWith(JsonPathConstants.Wildcard))
            {
                path = path[1..];
                segment = new JsonPathChildSegment<JsonPathWildcardChildSelector>(new JsonPathWildcardChildSelector());

                return true;
            }
            
            if (path.StartsWith(JsonPathConstants.ChildBracket))
            {
                var match = regex.ChildBracket.Match(path);

                if (match.Success)
                {
                    path = path[(match.Index + match.Length)..];
                    var matchValue = match.Groups[1].Value;
                    stringValue = matchValue;
                    
                    if (stringValue == JsonPathConstants.Wildcard)
                    {
                        segment = new JsonPathChildSegment<JsonPathWildcardChildSelector>(new JsonPathWildcardChildSelector());
                    }
                    else
                    {
                        var selectorMatches = new List<Match>();

                        foreach (var regexItem in new Regex[]
                        {
                            regex.EnclosureSingleQuote,
                            regex.EnclosureDoubleQuote,
                        })
                        {
                            var matches = regexItem.Matches(stringValue);
                            selectorMatches.AddRange(matches.Where(item => !string.IsNullOrEmpty(item.Groups[1].Value)).ToArray());
                        }

                        if (selectorMatches.Count > 0)
                        {
                            var selectors = new Dictionary<int, JsonPathChildSelector>();

                            foreach (var selectorMatchesItem in selectorMatches)
                            {
                                selectors.Add(selectorMatchesItem.Index, new JsonPathMemberNameChildSelector(selectorMatchesItem.Groups[1].Value));
                                matchValue = matchValue.Replace(selectorMatchesItem.Value, null);
                            }

                            var matchValueParts = matchValue.Split(JsonPathConstants.Union, StringSplitOptions.RemoveEmptyEntries);

                            if (matchValueParts.Length > 0) {
                                foreach (var matchValuePartsItem in matchValueParts)
                                {
                                    selectors.Add(stringValue.IndexOf(matchValuePartsItem), GetChildSelector(matchValuePartsItem));
                                }

                                segment = new JsonPathChildSegment<JsonPathUnionChildSelector>(new JsonPathUnionChildSelector([.. selectors.OrderBy(item => item.Key).Select(item => item.Value).ToArray()]));
                            }
                            else
                            {
                                segment = new JsonPathChildSegment<JsonPathMemberNameChildSelector>((JsonPathMemberNameChildSelector)selectors.First().Value);
                            }
                        }
                        else
                        {
                            foreach (var item in new string[]
                            {
                                JsonPathConstants.EnclosureSingleQuote,
                                JsonPathConstants.EnclosureDoubleQuote,
                            })
                            {
                                stringValue = stringValue.Replace($"{item}{item}", item);
                            }

                            segment = GetChildSegment(stringValue);
                        }
                    }

                    return true;
                }
            }
            
            stringValue = path.Split(
                [
                    JsonPathConstants.ChildDot,
                    JsonPathConstants.ChildBracket
                ],
                StringSplitOptions.RemoveEmptyEntries
            )[0];

            if (string.IsNullOrEmpty(stringValue) || stringValue.Length == 0)
            {
                return false;
            }

            segment = GetChildSegment(stringValue);
            path = path[stringValue.Length..];

            return true;
        }

        bool ValidateResultItem(JsonNode? node, out JsonValueKind valueKind, int? index = null)
        {
           void SetNullItem()
            {
                if (index.HasValue)
                {
                    result[index.Value] = null;
                }
            }
           
            valueKind = JsonValueKind.Undefined;

            if (node is null)
            {
                SetNullItem();

                return false;
            }

            valueKind = node.GetValueKind();

            if (valueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                SetNullItem();

                return false;
            }

            return true;
        }

        if (!ValidateResultItem(node, out var valueKind))
        {
            return [];
        }

        result.Add(node);

        bool shouldProceed;
        var hasDescendants = false;
        List<JsonNode> buffer;

        void SelectWildcard(JsonNode node, JsonValueKind valueKind, int index)
        {
            if (valueKind is JsonValueKind.Array)
            {
                buffer.AddRange(node.AsArray()!);
                shouldProceed = true;
            }
            else if (valueKind is JsonValueKind.Object)
            {
                buffer.AddRange(node.AsObject().Select(item => item.Value)!);
                shouldProceed = true;
            }

            result[index] = null;
        }

        void SelectArrayItem(JsonPathArrayIndexChildSelector selector, JsonNode node, JsonValueKind valueKind, int index)
        {
            if (valueKind == JsonValueKind.Array)
            {
                var arrayIndex = selector.Index;
                var arrayNode = node.AsArray();
                var arrayItem = arrayNode.ElementAtOrDefault(arrayIndex < 0 ? arrayNode.Count + arrayIndex : arrayIndex);

                if (!ValidateResultItem(arrayItem, out valueKind, index))
                {
                    return;
                }

                buffer.Add(arrayItem!);
                shouldProceed = true;
            }

            result[index] = null;
        }

        void SelectArraySlice(JsonPathArraySliceChildSelector selector, JsonNode node, JsonValueKind valueKind, int index)
        {
            if (valueKind == JsonValueKind.Array)
            {
                var step = selector.Step;

                if (step != 0)
                {
                    var arrayNode = node.AsArray();
                    var start = selector.Start;
                    var end = selector.End ?? arrayNode.Count;
                    var itemsAddedCount = 0;

                    void AddItem(int index)
                    {
                        if (index >= 0 || index < arrayNode.Count)
                        {
                            var node = arrayNode.ElementAtOrDefault(index);

                            if (node is not null)
                            {
                                buffer.Add(node);
                                itemsAddedCount++;
                            }
                        }
                    }

                    if (start < 0)
                    {
                        start = arrayNode.Count + start;
                    }

                    if (end < 0)
                    {
                        end = arrayNode.Count + end;
                    }

                    if (step < 0)
                    {
                        for (var itemIndex = end - 1; itemIndex >= start; itemIndex += step)
                        {
                            AddItem(itemIndex);
                        }
                    }
                    else
                    {
                        for (var itemIndex = start; itemIndex < end; itemIndex += step)
                        {
                            AddItem(itemIndex);
                        }
                    }

                    if (itemsAddedCount > 0)
                    {
                        shouldProceed = true;
                    }
                }
            }

            result[index] = null;
        }

        void SelectMember(JsonPathMemberNameChildSelector selector, JsonNode node, JsonValueKind valueKind, int index)
        {
            if (valueKind == JsonValueKind.Object)
            {
                var memberName = selector.MemberName;
                var member = node.AsObject().TryGetPropertyValue(memberName, out var value) ? value : default;

                if (!ValidateResultItem(member, out valueKind, index))
                {
                    return;
                }

                buffer.Add(member!);
                shouldProceed = true;
            }
            
            result[index] = null;
        }

        while (TryGetSegment(out var segment))
        {
            if (segment is not null)
            {
                shouldProceed = false;
                buffer = [];

                for (var resultIndex = 0; resultIndex < result.Count; resultIndex++)
                {
                    var resultItem = result[resultIndex];

                    if (!ValidateResultItem(resultItem, out valueKind, resultIndex))
                    {
                        continue;
                    }

                    switch (segment)
                    {
                        case JsonPathDescendantsSegment _:
                            if (!hasDescendants)
                            {
                                buffer.AddRange(resultItem!.GetDescendants());
                                result[resultIndex] = null;
                                shouldProceed = true;
                                hasDescendants = true;
                            }
                            break;
                        case JsonPathChildSegment<JsonPathWildcardChildSelector> _:
                            SelectWildcard(resultItem!, valueKind, resultIndex);
                            break;
                        case JsonPathChildSegment<JsonPathArrayIndexChildSelector> arrayIndexSegment:
                            SelectArrayItem(arrayIndexSegment.Selector, resultItem!, valueKind, resultIndex);    
                            continue;
                        case JsonPathChildSegment<JsonPathArraySliceChildSelector> arraySliceSegment:
                            SelectArraySlice(arraySliceSegment.Selector, resultItem!, valueKind, resultIndex);
                            continue;
                        case JsonPathChildSegment<JsonPathMemberNameChildSelector> memberNameSegment:
                            SelectMember(memberNameSegment.Selector, resultItem!, valueKind, resultIndex); 
                            continue;
                        case JsonPathChildSegment<JsonPathUnionChildSelector> unionSegment:
                            foreach (var selectorItem in unionSegment.Selector.Selectors)
                            {
                                switch (selectorItem)
                                {
                                    case JsonPathArrayIndexChildSelector arrayIndexSelector:
                                        SelectArrayItem(arrayIndexSelector, resultItem!, valueKind, resultIndex);
                                        break;
                                    case JsonPathArraySliceChildSelector arraySliceSelector:
                                        SelectArraySlice(arraySliceSelector, resultItem!, valueKind, resultIndex);
                                        break;
                                    case JsonPathMemberNameChildSelector memberNameSelector:
                                        SelectMember(memberNameSelector, resultItem!, valueKind, resultIndex);
                                        break;
                                    case JsonPathWildcardChildSelector _:
                                        SelectWildcard(resultItem!, valueKind, resultIndex);
                                        break;
                                }
                            }

                            continue;
                        default:
                            result[resultIndex] = null;
                            break;
                    }
                }

                if (!shouldProceed)
                {
                    return [];
                }

                result.AddRange(buffer);
            }

            result = [..GetResult()];
        }

        return GetResult();
    }

    public static JsonNodeSelectable ToSelectable(this JsonNode node, bool suppressException = true)
    {
        return new JsonNodeSelectable(node, suppressException);
    }

    #endregion Methods
}
