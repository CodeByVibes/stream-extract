namespace StreamExtract.Plugins;

public sealed class PluginRegistry
{
    private readonly Dictionary<string, IExtractorPlugin> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IExtractorPlugin> _plugins = [];

    public void Register(IExtractorPlugin plugin)
    {
        _plugins.Add(plugin);
        foreach (var ext in plugin.SupportedExtensions)
            _map[ext] = plugin;
    }

    public IExtractorPlugin? GetPlugin(string filePath)
        => _map.TryGetValue(Path.GetExtension(filePath), out var p) ? p : null;

    public IReadOnlyList<IExtractorPlugin> Plugins => _plugins;
}
