using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace StreamExtract.Services;

public sealed record UpdateInfo(Version LatestVersion, string DownloadUrl);

public sealed class UpdateChecker
{
    private const string GitHubRepo = "OWNER/REPO"; // TODO: set to your GitHub org/repo
    private const int MaxResponseBytes = 64 * 1024;

    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    static UpdateChecker()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StreamExtract-UpdateChecker");
    }

    private readonly string _updateUrl;
    private readonly Func<JsonDocument?, (string version, string url)> _parser;

    private UpdateChecker(string updateUrl, Func<JsonDocument?, (string version, string url)> parser)
    {
        _updateUrl = updateUrl;
        _parser = parser;
    }

    public static UpdateChecker CreateGitHub(string owner, string repo)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        return new UpdateChecker(url, json =>
        {
            var root = json!.RootElement;
            var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v');
            var htmlUrl = root.GetProperty("html_url").GetString()!;
            return (tag, htmlUrl);
        });
    }

    public static UpdateChecker CreateCustom(string url, Func<JsonDocument?, (string version, string url)> parser)
        => new(url, parser);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string body;
        try
        {
            using var response = await _httpClient.GetAsync(_updateUrl, HttpCompletionOption.ResponseContentRead, ct);
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxResponseBytes)
                throw new InvalidDataException($"Update response too large ({contentLength} bytes).");
            body = await response.Content.ReadAsStringAsync(ct);
            if (body.Length > MaxResponseBytes)
                throw new InvalidDataException($"Update response too large ({body.Length} bytes).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            Debug.WriteLine($"[UpdateChecker] Network failure: {ex.Message}");
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            var (remoteVersionStr, downloadUrl) = _parser(json);

            if (!Version.TryParse(remoteVersionStr, out var remoteVersion))
            {
                Debug.WriteLine($"[UpdateChecker] Failed to parse remote version: {remoteVersionStr}");
                return null;
            }

            var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (localVersion is null || remoteVersion <= localVersion)
                return null;

            return new UpdateInfo(remoteVersion, downloadUrl);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[UpdateChecker] Invalid JSON response: {ex.Message}");
            return null;
        }
    }
}
