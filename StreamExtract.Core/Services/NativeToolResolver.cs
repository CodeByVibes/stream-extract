namespace StreamExtract.Services;

public sealed class NativeToolResolver(string applicationBaseDirectory) : INativeToolResolver
{
    private readonly string _baseDirectory = Path.GetFullPath(applicationBaseDirectory);
    private readonly IReadOnlyDictionary<NativeToolId, string> _paths = CreatePaths(applicationBaseDirectory);

    public string Resolve(NativeToolId id)
    {
        if (!NativeTool.IsKnown(id))
            throw new NativeToolValidationException(NativeToolValidationFailure.UnknownId, id.ToString());
        return _paths.TryGetValue(id, out var path)
            ? path
            : throw new NativeToolValidationException(NativeToolValidationFailure.MissingRequired, id.ToString());
    }

    private static IReadOnlyDictionary<NativeToolId, string> CreatePaths(string baseDirectory)
    {
        var fullBase = Path.GetFullPath(baseDirectory);
        var manifest = NativeToolManifest.Load(fullBase);
        return NativeToolValidator.Validate(fullBase, manifest)
            .ToDictionary(x => x.Id, x => Path.Combine(fullBase, "tools", x.Filename));
    }
}
