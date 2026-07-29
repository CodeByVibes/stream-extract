namespace StreamExtract;

internal static class Program
{
    public static string ToolPath { get; private set; } = null!;

    [STAThread]
    static void Main()
    {
        ToolPath = Path.Combine(AppContext.BaseDirectory, "tools");
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
