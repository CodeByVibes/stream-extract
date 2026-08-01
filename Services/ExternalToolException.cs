namespace StreamExtract.Services;

public sealed class ExternalToolException : Exception
{
    public string ToolName { get; }
    public int ExitCode { get; }
    public string StandardError { get; }

    public ExternalToolException(string toolName, int exitCode, string standardError)
        : base(BuildMessage(toolName, exitCode, standardError))
    {
        ToolName = toolName;
        ExitCode = exitCode;
        StandardError = standardError;
    }

    private static string BuildMessage(string toolName, int exitCode, string standardError)
    {
        var err = standardError.Trim();
        if (err.Length > 500) err = err[..500] + "...";
        return $"'{toolName}' exited with code {exitCode}." +
            (err.Length > 0 ? $" {err}" : "");
    }
}
