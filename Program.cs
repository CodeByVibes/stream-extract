using StreamExtract.Services;

namespace StreamExtract;

internal static class Program
{
    public static string ToolPath { get; private set; } = null!;
    public static INativeToolResolver ToolResolver { get; private set; } = null!;

    private static int _unhandledExceptionShown;

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

        // ThreadException and AppDomain.UnhandledException can both fire for the same
        // failure; report the dialog only once.
        if (Interlocked.Exchange(ref _unhandledExceptionShown, 1) == 1)
            return;

        var logPath = GetErrorLogPath();
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\r\n\r\n");
        }
        catch
        {
            // Never let logging itself crash the process.
        }
        MessageBox.Show($"An unexpected error occurred:\r\n\r\n{ex.Message}",
            "StreamExtract Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string GetErrorLogPath()
    {
        try
        {
            // Prefer a per-user writable location over the app directory, which may be
            // read-only (e.g. installed under Program Files).
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(dir))
                return Path.Combine(dir, "StreamExtract", "stream-extract-error.log");
        }
        catch
        {
        }
        return Path.Combine(AppContext.BaseDirectory, "stream-extract-error.log");
    }

    private static bool ValidateTools(string toolPath)
    {
        var expectedPath = Path.GetFullPath(toolPath);
        var applicationBaseDirectory = Directory.GetParent(expectedPath)!.FullName;
        try
        {
            var manifest = NativeToolManifest.Load(applicationBaseDirectory);
            NativeToolValidator.Validate(applicationBaseDirectory, manifest);
            ToolResolver = new NativeToolResolver(applicationBaseDirectory);
            return true;
        }
        catch (NativeToolValidationException ex)
        {
            MessageBox.Show(
                $"The bundled native tools failed validation:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Tool Integrity Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
