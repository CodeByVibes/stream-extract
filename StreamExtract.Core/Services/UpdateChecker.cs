using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace StreamExtract.Services;

public sealed record UpdateInfo(Version LatestVersion, string DownloadUrl);

public sealed class UpdateChecker
{
    private const string GitHubRepo = "OWNER/REPO"; // TODO: set to your GitHub org/repo
    private const int MaxResponseBytes = 64 * 1024;

    private static readonly HttpClient _defaultHttpClient = CreateDefaultHttpClient();

    private readonly string _updateUrl;
    private readonly Func<JsonDocument?, (string version, string url)> _parser;
    private readonly HttpClient _httpClient;

    private UpdateChecker(string updateUrl, Func<JsonDocument?, (string version, string url)> parser,
        HttpClient? httpClient = null)
    {
        _updateUrl = updateUrl;
        _parser = parser;
        _httpClient = httpClient ?? _defaultHttpClient;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamExtract-UpdateChecker");
        return client;
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

    public static UpdateChecker CreateCustom(string url, Func<JsonDocument?, (string version, string url)> parser,
        HttpMessageHandler? handler = null)
        => new(url, parser, handler is null ? null : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) });

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string body;
        try
        {
            using var response = await _httpClient.GetAsync(_updateUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxResponseBytes)
            {
                Debug.WriteLine($"[UpdateChecker] Update response too large ({contentLength} bytes).");
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var bodyBuffer = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var readCount = await stream.ReadAsync(buffer.AsMemory(), ct);
                if (readCount == 0) break;
                if (bodyBuffer.Length + readCount > MaxResponseBytes)
                {
                    Debug.WriteLine($"[UpdateChecker] Update response too large (>{MaxResponseBytes} bytes).");
                    return null;
                }
                bodyBuffer.Write(buffer, 0, readCount);
            }
            body = Encoding.UTF8.GetString(bodyBuffer.GetBuffer(), 0, checked((int)bodyBuffer.Length));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateChecker] Update check failed: {ex.Message}");
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateChecker] Invalid update response: {ex.Message}");
            return null;
        }
    }
}
