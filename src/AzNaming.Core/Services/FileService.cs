namespace AzNaming.Core.Services;

public class FileService : IDisposable
{
    private readonly Lazy<string> _initializeTempDirectory = new(() =>
        {
            var result = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(result);

            return result;
        });

    private string? _tempPath = default;

    private readonly Lazy<HttpClient> _httpClient = new(() => new HttpClient());

    public async Task<string> DownloadFileAsync(Uri uri)
    {
        var tempPath = CreateTempDirectory();
        var filePath = Path.Combine(tempPath, $"{Path.GetFileNameWithoutExtension(uri.LocalPath)}_{Guid.NewGuid().ToString("N")}{Path.GetExtension(uri.LocalPath)}");

        using var stream = await _httpClient.Value.GetStreamAsync(uri);
        using var fileStream = new FileStream(filePath, FileMode.OpenOrCreate);

        await stream.CopyToAsync(fileStream);

        return filePath;
    }

    public string CreateTempDirectory()
    {
        _tempPath = _initializeTempDirectory.Value;

        return _tempPath;
    }

    public void Dispose()
    {
        if (_tempPath is not null && Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }

        _httpClient.Value.Dispose();

        GC.SuppressFinalize(this);
    }
}
