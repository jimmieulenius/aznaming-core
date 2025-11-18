using System.Text.Json.Nodes;
using AzNaming.Core.Extensions;
using System.Text.Json;

namespace AzNaming.Core.Test;

public class JsonNodeExtensionsTests
{
    private readonly JsonNode nodes = JsonNode.Parse("""
        {
            "phoneNumbers": [
                {
                    "type": "home",
                    "number": "123"
                },
                {
                    "type": "work",
                    "number": "456"
                },
                {
                    "type": "mobile",
                    "number": "789"
                }
            ],
            "address": {
                "primary": {
                    "city": "Berlin",
                    "streetAddress": "Main St"
                },
                "secondary": {
                    "city": "San Francisco",
                    "streetAddress": "Market St"
                }
            }
        }
        """)!;
    private readonly JsonSerializerOptions options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Theory]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("$..")]
    [InlineData("$...")]
    public void SelectNodes_Descendants(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(18, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes,
                nodes["phoneNumbers"]!,
                nodes["phoneNumbers"]![0]!,
                nodes["phoneNumbers"]![0]!["type"]!,
                nodes["phoneNumbers"]![0]!["number"]!,
                nodes["phoneNumbers"]![1]!,
                nodes["phoneNumbers"]![1]!["type"]!,
                nodes["phoneNumbers"]![1]!["number"]!,
                nodes["phoneNumbers"]![2]!,
                nodes["phoneNumbers"]![2]!["type"]!,
                nodes["phoneNumbers"]![2]!["number"]!,
                nodes["address"]!,
                nodes["address"]!["primary"]!,
                nodes["address"]!["primary"]!["city"]!,
                nodes["address"]!["primary"]!["streetAddress"]!,
                nodes["address"]!["secondary"]!,
                nodes["address"]!["secondary"]!["city"]!,
                nodes["address"]!["secondary"]!["streetAddress"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData("..phoneNumbers")]
    [InlineData("...phoneNumbers")]
    [InlineData("..[phoneNumbers]")]
    [InlineData("...[phoneNumbers]")]
    [InlineData("..['phoneNumbers']")]
    [InlineData("...['phoneNumbers']")]
    [InlineData("..[\"phoneNumbers\"]")]
    [InlineData("...[\"phoneNumbers\"]")]
    [InlineData("$..phoneNumbers")]
    [InlineData("$...phoneNumbers")]
    [InlineData("$..[phoneNumbers]")]
    [InlineData("$...[phoneNumbers]")]
    [InlineData("$..['phoneNumbers']")]
    [InlineData("$...['phoneNumbers']")]
    [InlineData("$..[\"phoneNumbers\"]")]
    [InlineData("$...[\"phoneNumbers\"]")]
    public void SelectNodes_Descendants_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Single(selectedNodes);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".")]
    [InlineData("$.")]
    public void SelectNodes_Child(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Single(selectedNodes);
        Assert.Equal(
            ToJsonString(
                nodes
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers")]
    [InlineData(".[phoneNumbers]")]
    [InlineData(".['phoneNumbers']")]
    [InlineData(".[\"phoneNumbers\"]")]
    [InlineData("[phoneNumbers]")]
    [InlineData("['phoneNumbers']")]
    [InlineData("[\"phoneNumbers\"]")]
    [InlineData("$.phoneNumbers")]
    [InlineData("$.[phoneNumbers]")]
    [InlineData("$.['phoneNumbers']")]
    [InlineData("$.[\"phoneNumbers\"]")]
    [InlineData("$[phoneNumbers]")]
    [InlineData("$['phoneNumbers']")]
    [InlineData("$[\"phoneNumbers\"]")]
    public void SelectNodes_Child_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Single(selectedNodes);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".address.primary")]
    [InlineData(".[address].[primary]")]
    [InlineData(".['address'].[primary]")]
    [InlineData(".[\"address\"].[\"primary\"]")]
    [InlineData("[address][primary]")]
    [InlineData("['address']['primary']")]
    [InlineData("[\"address\"][\"primary\"]")]
    [InlineData("$.address.primary")]
    [InlineData("$.[address].[primary]")]
    [InlineData("$.['address'].[primary]")]
    [InlineData("$.[\"address\"].[\"primary\"]")]
    [InlineData("$[address][primary]")]
    [InlineData("$['address']['primary']")]
    [InlineData("$[\"address\"][\"primary\"]")]
    public void SelectNodes_ChildNested_Address_Primary(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Single(selectedNodes);
        Assert.Equal(
            ToJsonString(
                nodes["address"]!["primary"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData("..[phoneNumbers,address]")]
    [InlineData("..['phoneNumbers','address']")]
    [InlineData("..[\"phoneNumbers\",\"address\"]")]
    [InlineData("$..[phoneNumbers,address]")]
    [InlineData("$..['phoneNumbers','address']")]
    [InlineData("$..[\"phoneNumbers\",\"address\"]")]
    public void SelectNodes_Union_PhoneNumbersAndAddress(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]!,
                nodes["address"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData("..*")]
    [InlineData("..[*]")]
    [InlineData("..['*']")]
    [InlineData("..[\"*\"]")]
    [InlineData("$..*")]
    [InlineData("$..[*]")]
    [InlineData("$..['*']")]
    [InlineData("$..[\"*\"]")]
    public void SelectNodes_Wildcard(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(17, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]!,
                nodes["address"]!,
                nodes["phoneNumbers"]![0]!,
                nodes["phoneNumbers"]![1]!,
                nodes["phoneNumbers"]![2]!,
                nodes["phoneNumbers"]![0]!["type"]!,
                nodes["phoneNumbers"]![0]!["number"]!,
                nodes["phoneNumbers"]![1]!["type"]!,
                nodes["phoneNumbers"]![1]!["number"]!,
                nodes["phoneNumbers"]![2]!["type"]!,
                nodes["phoneNumbers"]![2]!["number"]!,
                nodes["address"]!["primary"]!,
                nodes["address"]!["secondary"]!,
                nodes["address"]!["primary"]!["city"]!,
                nodes["address"]!["primary"]!["streetAddress"]!,
                nodes["address"]!["secondary"]!["city"]!,
                nodes["address"]!["secondary"]!["streetAddress"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData("..*.type")]
    [InlineData("..[*].[type]")]
    [InlineData("..['*'].['type']")]
    [InlineData("..[\"*\"].[\"type\"]")]
    [InlineData("..[*][type]")]
    [InlineData("..['*']['type']")]
    [InlineData("..[\"*\"][\"type\"]")]
    [InlineData("$..*.type")]
    [InlineData("$..[*].[type]")]
    [InlineData("$..['*'].['type']")]
    [InlineData("$..[\"*\"].[\"type\"]")]
    [InlineData("$..[*][type]")]
    [InlineData("$..['*']['type']")]
    [InlineData("$..[\"*\"][\"type\"]")]
    public void SelectNodes_Wildcard_Type(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(3, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![0]!["type"]!,
                nodes["phoneNumbers"]![1]!["type"]!,
                nodes["phoneNumbers"]![2]!["type"]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.0")]
    [InlineData(".[phoneNumbers].[0]")]
    [InlineData("[phoneNumbers][0]")]
    [InlineData("$.phoneNumbers.0")]
    [InlineData("$.[phoneNumbers].[0]")]
    [InlineData("$[phoneNumbers][0]")]
    public void SelectNodes_ArrayIndex_0_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Single(selectedNodes);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![0]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.-2")]
    [InlineData(".[phoneNumbers].[-2]")]
    [InlineData("[phoneNumbers][-2]")]
    [InlineData("$.phoneNumbers.-2")]
    [InlineData("$.[phoneNumbers].[-2]")]
    [InlineData("$[phoneNumbers][-2]")]
    public void SelectNodes_ArrayIndex_Minus2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Single(selectedNodes);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![1]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[0,2]")]
    [InlineData(".[phoneNumbers].[0,2]")]
    [InlineData("[phoneNumbers][0,2]")]
    [InlineData("$.phoneNumbers.[0,2]")]
    [InlineData("$.[phoneNumbers].[0,2]")]
    [InlineData("$[phoneNumbers][0,2]")]
    public void SelectNodes_ArrayIndex_0And2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![0]!,
                nodes["phoneNumbers"]![2]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[0:2]")]
    [InlineData(".[phoneNumbers].[0:2]")]
    [InlineData("[phoneNumbers][0:2]")]
    [InlineData("$.phoneNumbers.[0:2]")]
    [InlineData("$.[phoneNumbers].[0:2]")]
    [InlineData("$[phoneNumbers][0:2]")]
    public void SelectNodes_ArraySlice_0_2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![0]!,
                nodes["phoneNumbers"]![1]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[:2]")]
    [InlineData(".[phoneNumbers].[:2]")]
    [InlineData("[phoneNumbers][:2]")]
    [InlineData("$.phoneNumbers.[:2]")]
    [InlineData("$.[phoneNumbers].[:2]")]
    [InlineData("$[phoneNumbers][:2]")]
    public void SelectNodes_ArraySlice_Default_2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![0]!,
                nodes["phoneNumbers"]![1]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[-2:2]")]
    [InlineData(".[phoneNumbers].[-2:2]")]
    [InlineData("[phoneNumbers][-2:2]")]
    [InlineData("$.phoneNumbers.[-2:2]")]
    [InlineData("$.[phoneNumbers].[-2:2]")]
    [InlineData("$[phoneNumbers][-2:2]")]
    public void SelectNodes_ArraySlice_Minus2_2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![1]!,
                nodes["phoneNumbers"]![2]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[-2:]")]
    [InlineData(".[phoneNumbers].[-2:]")]
    [InlineData("[phoneNumbers][-2:]")]
    [InlineData("$.phoneNumbers.[-2:]")]
    [InlineData("$.[phoneNumbers].[-2:]")]
    [InlineData("$[phoneNumbers][-2:]")]
    public void SelectNodes_ArraySlice_Minus2_Default_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![1]!,
                nodes["phoneNumbers"]![2]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[0:3:2]")]
    [InlineData(".[phoneNumbers].[0:3:2]")]
    [InlineData("[phoneNumbers][0:3:2]")]
    [InlineData("$.phoneNumbers.[0:3:2]")]
    [InlineData("$.[phoneNumbers].[0:3:2]")]
    [InlineData("$[phoneNumbers][0:3:2]")]
    public void SelectNodes_ArraySlice_0_3_2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![0]!,
                nodes["phoneNumbers"]![2]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[-2::-1]")]
    [InlineData(".[phoneNumbers].[-2::-1]")]
    [InlineData("[phoneNumbers][-2::-1]")]
    [InlineData("$.phoneNumbers.[-2::-1]")]
    [InlineData("$.[phoneNumbers].[-2::-1]")]
    [InlineData("$[phoneNumbers][-2::-1]")]
    public void SelectNodes_ArraySlice_Minus2_Default_Minus1_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![2]!,
                nodes["phoneNumbers"]![1]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    [Theory]
    [InlineData(".phoneNumbers.[::-2]")]
    [InlineData(".[phoneNumbers].[::-2]")]
    [InlineData("[phoneNumbers][::-2]")]
    [InlineData("$.phoneNumbers.[::-2]")]
    [InlineData("$.[phoneNumbers].[::-2]")]
    [InlineData("$[phoneNumbers][::-2]")]
    public void SelectNodes_ArraySlice_Default_Default_Minus2_PhoneNumbers(string jsonPath)
    {
        var selectedNodes = nodes.SelectNodes(jsonPath);
        Assert.NotNull(selectedNodes);
        Assert.Equal(2, selectedNodes.Length);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![2]!,
                nodes["phoneNumbers"]![0]!
            ),
            ToJsonString(
                selectedNodes
            )
        );
    }

    // [Theory]
    // [InlineData(".phoneNumbers")]
    // // [InlineData("$.[phoneNumbers].[::-2]")]
    // // [InlineData("$[phoneNumbers][::-2]")]
    // public void SelectNodes_Invalid(string jsonPath)
    // {
    //     var selectedNodes = nodes.SelectNodes(jsonPath);
    //     Assert.NotNull(selectedNodes);
    //     Assert.Equal(2, selectedNodes.Length);
    //     Assert.Equal(
    //         ToJsonString(),
    //         ToJsonString(
    //             selectedNodes
    //         )
    //     );
    // }

    [Fact]
    public void ToSelectable_Node_PhoneNumbers()
    {
        var selectable = nodes.ToSelectable()["phoneNumbers"];
        Assert.NotNull(selectable.Node);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]!
            ),
            ToJsonString(
                selectable.Node!
            )
        );
    }

    [Fact]
    public void ToSelectable_Node_ArrayIndex_1_PhoneNumbers()
    {
        var selectable = nodes.ToSelectable()["phoneNumbers"][1];
        Assert.NotNull(selectable.Node);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![1]!
            ),
            ToJsonString(
                selectable.Node!
            )
        );
    }

    [Fact]
    public void ToSelectable_Node_ArrayIndex_Minus1_PhoneNumbers()
    {
        var selectable = nodes.ToSelectable()["phoneNumbers"][-1];
        Assert.NotNull(selectable.Node);
        Assert.Equal(
            ToJsonString(
                nodes["phoneNumbers"]![2]!
            ),
            ToJsonString(
                selectable.Node!
            )
        );
    }

    // [Fact]
    // public void ToSelectable_Slice_Minus2_2_PhoneNumbers()
    // {
    //     var selectedNodes = nodes.ToSelectable()["phoneNumbers"][-2, 2];
    //     Assert.NotNull(selectedNodes);
    //     Assert.Equal(
    //         ToJsonString(
    //             nodes["phoneNumbers"]![1]!,
    //             nodes["phoneNumbers"]![2]!
    //         ),
    //         JsonSerializer.Serialize(
    //             selectedNode,
    //             options
    //         )
    //     );
    // }

    private string ToJsonString(params JsonNode[] nodes)
    {
        return JsonSerializer.Serialize(nodes, options);
    }

    // [Fact]
    // public void SelectNodes_AllCases()
    // {
    //     var nodes1 = nodes.SelectNodes("$..");
    //     Assert.NotNull(nodes1);
    //     Assert.True(nodes1.Length > 0);

    //     var nodes2 = nodes.SelectNodes("$..phoneNumbers");
    //     Assert.NotNull(nodes2);
    //     Assert.Single(nodes2);
    //     Assert.Equal(
    //         """
    //         [
    //             {
    //                 "type": "home",
    //                 "number": "123"
    //             },
    //             {
    //                 "type": "work",
    //                 "number": "456"
    //             },
    //             {
    //                 "type": "mobile",
    //                 "number": "789"
    //             }
    //         ]
    //         """,
    //         nodes2[0].ToJsonString(options)
    //     );

    //     var nodes2_1 = value.SelectNodes("$..[phoneNumbers]");
    //     Assert.NotNull(nodes2_1);
    //     Assert.Single(nodes2_1);

    //     var nodes2_5 = value.SelectNodes("$...[phoneNumbers]");
    //     Assert.NotNull(nodes2_5);

    //     var nodes2_6 = value.SelectNodes("$....[phoneNumbers]");
    //     Assert.NotNull(nodes2_6);

    //     var nodes2_2 = value.SelectNodes("$..['phoneNumbers']");
    //     Assert.NotNull(nodes2_2);
    //     Assert.Single(nodes2_2);

    //     var nodes2_3 = value.SelectNodes("$..[\"phoneNumbers\"]");
    //     Assert.NotNull(nodes2_3);
    //     Assert.Single(nodes2_3);

    //     var nodes2_4 = value.SelectNodes("$..[\"phoneNumbers\",'address'].[-3::2,'city','streetaddress']");
    //     Assert.NotNull(nodes2_4);

    //     var nodes3 = value.SelectNodes("$.*");
    //     Assert.NotNull(nodes3);
    //     Assert.True(nodes3.Length >= 3);

    //     var nodes4 = value.SelectNodes("$.*.*.type");
    //     Assert.NotNull(nodes4);

    //     var nodes5 = value.SelectNodes("$.phoneNumbers[0]");
    //     Assert.NotNull(nodes5);
    //     Assert.Single(nodes5);
    //     Assert.Equal("home", nodes5[0]["type"]!.GetValue<string>());

    //     var nodes6 = value.SelectNodes("$.phoneNumbers[-2]");
    //     Assert.NotNull(nodes6);
    //     Assert.Single(nodes6);
    //     Assert.Equal("work", nodes6[0]["type"]!.GetValue<string>());

    //     var nodes7 = value.SelectNodes("$.phoneNumbers[0:3]");
    //     Assert.NotNull(nodes7);
    //     Assert.Equal(3, nodes7.Length);

    //     var nodes8 = value.SelectNodes("$.phoneNumbers[0:3:2]");
    //     Assert.NotNull(nodes8);
    //     Assert.Equal(2, nodes8.Length);

    //     var nodes9 = value.SelectNodes("$.phoneNumbers[0:3:-1]");
    //     Assert.NotNull(nodes9);
    //     Assert.Empty(nodes9);

    //     var nodes10 = value.SelectNodes("$.phoneNumbers[-2:]");
    //     Assert.NotNull(nodes10);
    //     Assert.Equal(2, nodes10.Length);

    //     var nodes11 = value.SelectNodes("$.phoneNumbers[-2::-1]");
    //     Assert.NotNull(nodes11);

    //     var nodes12 = value.SelectNodes("$.phoneNumbers[-2::0]");
    //     Assert.NotNull(nodes12);
    // }

    // [Fact]
    // public void ToSelectable_NodeAndExist_Cases()
    // {
    //     var node = value.ToSelectable()["object"]["prop1"][0].Node;
    //     Assert.NotNull(node);
    //     Assert.Equal("zero", node!.GetValue<string>());

    //     var node1 = value.ToSelectable()["phoneNumbers"][0].Node;
    //     Assert.NotNull(node1);
    //     Assert.Equal("home", node1!["type"]!.GetValue<string>());

    //     var node2 = value.ToSelectable()["phoneNumbers"][-1].Node;
    //     Assert.NotNull(node2);
    //     Assert.Equal("mobile", node2!["type"]!.GetValue<string>());

    //     var exist = value.ToSelectable()["object"]["prop1"][0].Exist;
    //     Assert.True(exist);

    //     var exist2 = value.ToSelectable()["phoneNumbers"][10].Exist;
    //     Assert.False(exist2);
    // }
}