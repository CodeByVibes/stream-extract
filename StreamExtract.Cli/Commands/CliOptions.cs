namespace StreamExtract.Cli.Commands;

public enum CliCommand { Help, Version, Info, Extract }
public enum CliParseFailure { None, Usage }

public sealed record CliParseResult(CliCommand Command, IReadOnlyList<string> InputFiles,
    IReadOnlyList<int> TrackIds, bool ExtractChapters, bool ExtractAttachments, bool ExtractTags,
    bool ExtractCueSheets, bool ExtractCuesForSelectedTracks, bool ExtractTimestamps, bool All,
    string? OutputDirectory, bool Verbose, CliParseFailure Failure = CliParseFailure.None, string? Error = null);

public static class CliOptions
{
    public const string HelpText = "Usage: streamextract info <file> | streamextract extract <file...> [options]";
    public const string Version = "1.0.0";
}
