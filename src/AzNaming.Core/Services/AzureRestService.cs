using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Azure.Identity;

namespace AzNaming.Core.Services;

public class AzureRestService : IDisposable
{
    private const string _azureFederatedToken = "azure_federated_token";
    private readonly string _federatedTokenFilePath = Path.Combine(Path.GetTempPath(), _azureFederatedToken);

    private readonly Lazy<HttpClient> _httpClient = new(() =>
    {
        var environmentVariableName = new
        {
            AzureFederatedToken = "AZURE_FEDERATED_TOKEN",
            AzureFederatedTokenFile = "AZURE_FEDERATED_TOKEN_FILE"
        };
        var azureUri = new
        {
            Management = "https://management.azure.com/"
        };

        var httpClient = new HttpClient();
        var federatedToken = Environment.GetEnvironmentVariable(environmentVariableName.AzureFederatedToken);

            if (!string.IsNullOrEmpty(federatedToken))
            {
                var path = Path.Combine(Path.GetTempPath(), _azureFederatedToken);
                using var file = new FileStream(path, FileMode.Create);
                File.WriteAllText(path, federatedToken);

                Environment.SetEnvironmentVariable(environmentVariableName.AzureFederatedTokenFile, path);
            }

            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                new DefaultAzureCredential().GetToken(new TokenRequestContext([$"{azureUri.Management}/.default"])).Token);

        return httpClient;
    });

    public async Task<HttpResponseMessage> GetAsync(string uri)
    {
        return (await _httpClient.Value.GetAsync(uri)).EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> PostAsync(string uri, string body)
    {
        return (await _httpClient.Value.PostAsync(uri, new StringContent(body, Encoding.UTF8, "application/json"))).EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> InvokeAsync(Func<HttpClient, Task<HttpResponseMessage>> action)
    {
        return await action(_httpClient.Value);
    }

    public void Dispose()
    {
        if (_httpClient.IsValueCreated)
        {
            _httpClient.Value.Dispose();
        }

        if (File.Exists(_federatedTokenFilePath))
        {
            File.Delete(_federatedTokenFilePath);
        }

        GC.SuppressFinalize(this);
    }
}
