namespace StreamExtract;

internal static class Program
{
    private static readonly string[] _requiredTools = { "mp4box.exe", "mkvmerge.exe", "mkvextract.exe" };

    public static string ToolPath { get; private set; } = null!;

    [STAThread]
    static void Main()
    {
        ToolPath = Path.Combine(AppContext.BaseDirectory, "tools");

        if (!ValidateTools(ToolPath))
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    private static bool ValidateTools(string toolPath)
    {
        var missing = _requiredTools
            .Where(tool => !File.Exists(Path.Combine(toolPath, tool)))
            .ToList();
        if (missing.Count == 0) return true;

        var expectedPath = Path.GetFullPath(toolPath);
        var list = string.Join(Environment.NewLine, missing.Select(t => $"  {t} (expected in {expectedPath})"));
        MessageBox.Show(
            $"Required native tools are missing:{Environment.NewLine}{Environment.NewLine}{list}" +
            $"{Environment.NewLine}{Environment.NewLine}The application cannot start without them.",
            "Missing Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }
}
