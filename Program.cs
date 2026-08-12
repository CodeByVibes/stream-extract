namespace StreamExtract;

internal static class Program
{
    // SHA-256 of the bundled tools in tools/. The app fails closed if a binary does not
    // match, so a tampered or accidentally corrupted tool cannot run.
    // Update these when a bundled tool is replaced or upgraded:
    //   sha256sum tools/<name>     (or: Get-FileHash tools\<name> -Algorithm SHA256)
    private static readonly Dictionary<string, string> _expectedToolHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mkvmerge.exe"] = "53698c4cac3ed89cfed2376115ab4ebaf52c4e7c298cc0c307ae65a874dce83d",
        ["mkvextract.exe"] = "84d080d6ff767f2d35e3ca9606cf7a7033fde5e3fe7fae30803e92189a7903ea",
        ["mp4box.exe"] = "40da30d87fa2401b1b1934f17bd672ca08f732e55a9e59c2c235541668db22a7",
    };

    public static string ToolPath { get; private set; } = null!;

    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleUnexpectedException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleUnexpectedException(e.ExceptionObject as Exception);

        ToolPath = Path.Combine(AppContext.BaseDirectory, "tools");

        if (!ValidateTools(ToolPath))
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    private static void HandleUnexpectedException(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "stream-extract-error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\r\n\r\n");
        }
        catch
        {
            // Never let logging itself crash the process.
        }
        MessageBox.Show($"An unexpected error occurred:\r\n\r\n{ex.Message}",
            "StreamExtract Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static bool ValidateTools(string toolPath)
    {
        var expectedPath = Path.GetFullPath(toolPath);
        var missing = _expectedToolHashes.Keys
            .Where(tool => !File.Exists(Path.Combine(toolPath, tool)))
            .ToList();
        if (missing.Count > 0)
        {
            var list = string.Join(Environment.NewLine, missing.Select(t => $"  {t} (expected in {expectedPath})"));
            MessageBox.Show(
                $"Required native tools are missing:{Environment.NewLine}{Environment.NewLine}{list}" +
                $"{Environment.NewLine}{Environment.NewLine}The application cannot start without them.",
                "Missing Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        var mismatched = new List<string>();
        foreach (var (tool, expectedHash) in _expectedToolHashes)
        {
            var path = Path.Combine(toolPath, tool);
            string actualHash;
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                actualHash = Convert.ToHexString(sha.ComputeHash(stream));
            }
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                mismatched.Add($"  {tool}: expected {expectedHash}, got {actualHash}");
        }

        if (mismatched.Count > 0)
        {
            MessageBox.Show(
                $"The bundled native tools failed their integrity check:{Environment.NewLine}{Environment.NewLine}" +
                $"{string.Join(Environment.NewLine, mismatched)}" +
                $"{Environment.NewLine}{Environment.NewLine}The application will not start. " +
                "If you replaced or upgraded the tools, update the expected hashes in Program.cs.",
                "Tool Integrity Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        return true;
    }
}
