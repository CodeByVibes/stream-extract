using StreamExtract.Cli.Commands;
using StreamExtract.Cli.Extraction;
using StreamExtract.Plugins;
using StreamExtract.Services;

var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler? handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += handler;
try
{
    CliParseResult options;
    try { options = CliParser.TryParse(args); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
    if (options.Failure != CliParseFailure.None) { Console.Error.WriteLine(options.Error); Console.Error.WriteLine(CliOptions.HelpText); return 2; }
    if (options.Command is CliCommand.Help or CliCommand.Version)
        return await new CommandRunner(new PluginRegistry(), Console.Out, Console.Error).RunAsync(options, cancellation.Token);
    var resolver = new NativeToolResolver(AppContext.BaseDirectory);
    var registry = new PluginRegistry();
    registry.Register(new MkvExtractorPlugin(resolver));
    registry.Register(new Mp4ExtractorPlugin(resolver));
    return await new CommandRunner(registry, Console.Out, Console.Error).RunAsync(options, cancellation.Token);
}
catch (OperationCanceledException) { return 130; }
catch (NativeToolValidationException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
finally { Console.CancelKeyPress -= handler; cancellation.Dispose(); }
