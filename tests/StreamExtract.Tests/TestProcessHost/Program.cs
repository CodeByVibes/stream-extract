using System.Globalization;
using System.Diagnostics;

var options = ParseOptions(args);

if (options.ContainsKey("child"))
{
    WritePid(options);
    WriteReady(options);
    await Delay(options);
    return 0;
}

Process? child = null;
if (options.TryGetValue("spawn-child", out var childReadyFile))
{
    var childPidFile = options.GetValueOrDefault("child-pid-file");
    var childArgs = new List<string> { Environment.ProcessPath is null ? throw new InvalidOperationException() : Path.Combine(AppContext.BaseDirectory, "TestProcessHost.dll"), "--child", "--ready-file", childReadyFile };
    if (childPidFile is not null)
        childArgs.AddRange(["--pid-file", childPidFile]);
    if (options.TryGetValue("sleep-ms", out var childSleep))
        childArgs.AddRange(["--sleep-ms", childSleep]);
    var childStartInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath,
        UseShellExecute = false
    };
    foreach (var arg in childArgs)
        childStartInfo.ArgumentList.Add(arg);
    child = Process.Start(childStartInfo);
    if (child is null)
        throw new InvalidOperationException("Unable to start child process.");
    await WaitForFileAsync(childReadyFile);
}

WritePid(options);
WriteReady(options);

if (options.TryGetValue("stdout", out var stdout))
    Console.Out.Write(stdout);
if (options.TryGetValue("stderr", out var stderr))
    Console.Error.Write(stderr);

await Delay(options);

return options.TryGetValue("exit-code", out var exitText) &&
       int.TryParse(exitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode)
    ? exitCode
    : 0;

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Options must be supplied as --name value pairs.");

        if (args[i] == "--child")
        {
            options["child"] = "true";
            continue;
        }

        if (i + 1 >= args.Length)
            throw new ArgumentException("Options must be supplied as --name value pairs.");

        options[args[i][2..]] = args[++i];
    }

    return options;
}

static void WritePid(IReadOnlyDictionary<string, string> options)
{
    if (options.TryGetValue("pid-file", out var pidFile))
        File.WriteAllText(pidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
}

static void WriteReady(IReadOnlyDictionary<string, string> options)
{
    if (options.TryGetValue("ready-file", out var readyFile))
        File.WriteAllText(readyFile, "ready");
}

static async Task Delay(IReadOnlyDictionary<string, string> options)
{
    if (options.TryGetValue("sleep-ms", out var sleepText) &&
        int.TryParse(sleepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sleepMilliseconds))
        await Task.Delay(sleepMilliseconds);
}

static async Task WaitForFileAsync(string path)
{
    while (!File.Exists(path))
        await Task.Delay(10);
}
