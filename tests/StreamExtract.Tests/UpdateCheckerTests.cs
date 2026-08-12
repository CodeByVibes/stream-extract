using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using StreamExtract.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Tests for <see cref="UpdateChecker"/> against a local HTTP server, covering the
/// fail-closed contract: only cancellation may propagate, everything else yields null.
/// </summary>
public class UpdateCheckerTests
{
    private sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;
        private readonly int _status;
        private volatile bool _disposed;

        public MiniHttpServer(byte[] body, int status = 200)
        {
            _body = body;
            _status = status;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Task.Run(AcceptLoopAsync);
        }

        public string Url { get; }

        private async Task AcceptLoopAsync()
        {
            while (!_disposed)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch { return; }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                // Read the request head (headers end with CRLFCRLF).
                var buffer = new byte[8192];
                var total = 0;
                while (total < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
                    if (n == 0) break;
                    total += n;
                    if (total >= 4 && buffer.AsSpan(total - 4, 4).SequenceEqual("\r\n\r\n"u8)) break;
                }

                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {_status} OK\r\nContent-Type: application/json\r\n" +
                    $"Content-Length: {_body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(_body);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _listener.Stop();
        }
    }

    private static UpdateChecker CheckerFor(string url)
        => UpdateChecker.CreateCustom(url, json =>
            (json!.RootElement.GetProperty("version").GetString()!,
             json.RootElement.GetProperty("url").GetString()!));

    private static MiniHttpServer JsonServer(string version, string url = "https://cudacoder.com/dl")
        => new(Encoding.UTF8.GetBytes($"{{\"version\":\"{version}\",\"url\":\"{url}\"}}"));

    [Fact]
    public async Task NewerVersion_ReturnsUpdateInfo()
    {
        using var server = JsonServer("9.9.9");
        var update = await CheckerFor(server.Url).CheckAsync();

        Assert.NotNull(update);
        Assert.Equal(new Version(9, 9, 9), update!.LatestVersion);
        Assert.Equal("https://cudacoder.com/dl", update.DownloadUrl);
    }

    [Fact]
    public async Task OlderVersion_ReturnsNull()
    {
        using var server = JsonServer("0.0.1");
        Assert.Null(await CheckerFor(server.Url).CheckAsync());
    }

    [Fact]
    public async Task EqualVersion_ReturnsNull()
    {
        var local = Assembly.GetAssembly(typeof(UpdateChecker))!.GetName().Version!.ToString();
        using var server = JsonServer(local);
        Assert.Null(await CheckerFor(server.Url).CheckAsync());
    }

    [Fact]
    public async Task OversizedResponse_ReturnsNull()
    {
        var body = Encoding.UTF8.GetBytes(new string('x', 70 * 1024));
        using var server = new MiniHttpServer(body);
        Assert.Null(await CheckerFor(server.Url).CheckAsync());
    }

    [Fact]
    public async Task MalformedJson_ReturnsNull()
    {
        using var server = new MiniHttpServer(Encoding.UTF8.GetBytes("this is not json"));
        Assert.Null(await CheckerFor(server.Url).CheckAsync());
    }

    [Fact]
    public async Task UnreachableServer_ReturnsNull()
    {
        var checker = CheckerFor("http://127.0.0.1:1/");
        Assert.Null(await checker.CheckAsync());
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsOperationCanceled()
    {
        using var server = JsonServer("9.9.9");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CheckerFor(server.Url).CheckAsync(cts.Token));
    }
}
