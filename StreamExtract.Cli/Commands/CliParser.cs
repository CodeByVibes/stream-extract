namespace StreamExtract.Cli.Commands;

public static class CliParser
{
    public static CliParseResult Parse(string[] args)
    {
        var result = TryParse(args);
        return result.Failure == CliParseFailure.None ? result : throw new CliUsageException(result.Error!);
    }

    public static CliParseResult TryParse(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h") return Empty(CliCommand.Help);
        if (args.Length == 1 && args[0] == "--version") return Empty(CliCommand.Version);
        if (args.Length < 2 || args[0] is not ("info" or "extract")) return Invalid("expected info or extract command");

        var command = args[0] == "info" ? CliCommand.Info : CliCommand.Extract;
        var files = new List<string>();
        var tracks = new List<int>();
        var switches = new HashSet<string>(StringComparer.Ordinal);
        var chapters = false; var attachments = false; var tags = false; var cues = false;
        var selectedCues = false; var timestamps = false; var all = false; var verbose = false;
        string? output = null;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-')) { files.Add(arg); continue; }
            switch (arg)
            {
                case "--tracks" when command == CliCommand.Extract:
                    if (!switches.Add(arg)) return Invalid("duplicate --tracks");
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) return Invalid("--tracks requires IDs");
                    foreach (var value in args[i].Split(','))
                    {
                        if (!int.TryParse(value, out var id) || id < 0) return Invalid("invalid track IDs");
                        if (tracks.Contains(id)) return Invalid("duplicate track ID");
                        tracks.Add(id);
                    }
                    break;
                case "--chapters" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --chapters"); chapters = true; break;
                case "--attachments" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --attachments"); attachments = true; break;
                case "--tags" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --tags"); tags = true; break;
                case "--cue-sheets" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --cue-sheets"); cues = true; break;
                case "--cues-for-selected-tracks" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --cues-for-selected-tracks"); selectedCues = true; break;
                case "--timestamps" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --timestamps"); timestamps = true; break;
                case "--all" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --all"); all = true; break;
                case "--verbose" when command == CliCommand.Extract: if (!switches.Add(arg)) return Invalid("duplicate --verbose"); verbose = true; break;
                case "--output" when command == CliCommand.Extract:
                    if (!switches.Add(arg)) return Invalid("duplicate --output");
                    if (++i >= args.Length || args[i].StartsWith('-') || string.IsNullOrWhiteSpace(args[i])) return Invalid("--output requires a directory");
                    if (output is not null) return Invalid("duplicate --output"); output = args[i]; break;
                default: return Invalid($"unknown or misplaced option '{arg}'");
            }
        }
        if (files.Count == 0) return Invalid("at least one input file is required");
        if (command == CliCommand.Info && files.Count != 1) return Invalid("info accepts exactly one file");
        if (command == CliCommand.Extract && !all && tracks.Count == 0 && !chapters && !attachments && !tags && !cues && !selectedCues && !timestamps)
            return Invalid("select at least one extraction mode");
        if (all && (tracks.Count > 0 || chapters || attachments || tags || cues || selectedCues || timestamps))
            return Invalid("--all cannot be combined with another extraction mode");
        return new(command, files, tracks, chapters, attachments, tags, cues, selectedCues, timestamps, all, output, verbose);
    }

    private static CliParseResult Empty(CliCommand command) => new(command, [], [], false, false, false, false, false, false, false, null, false);
    private static CliParseResult Invalid(string error) => new(CliCommand.Help, [], [], false, false, false, false, false, false, false, null, false, CliParseFailure.Usage, error);
}

public sealed class CliUsageException(string message) : Exception(message);
