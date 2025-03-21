namespace AzNaming.Core.Models;

public class NamingInvokeOptions
{
    public bool NoAdditionalValues { get; set; }

    public bool CheckUniqueName { get; set; }

    public bool AllowTruncation { get; set; }

    public bool ClearConfig { get; set; }

    public bool SuppressError { get; set; }

    public string[] ConfigUri { get; set; } = [];

    public string? SubscriptionId { get; set; }

    public string? ResourceGroupName { get; set; }

    public string? Location { get; set; }
}
