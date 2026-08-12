namespace StreamExtract.Plugins;

/// <summary>
/// Maps mp4box "Media Type: &lt;handler&gt;:&lt;codec&gt;" codec strings to output file
/// extensions, mirroring <see cref="MkvCodecExtensions"/> for MKV. Fallback is "bin".
/// </summary>
internal static class Mp4CodecExtensions
{
    public static string GetExtension(string codec) => codec.ToLowerInvariant() switch
    {
        "avc1" or "avc3" => "h264",
        "hvc1" or "hev1" => "h265",
        "mp4v" => "m4v",
        "mp4a" => "aac",
        "opus" => "opus",
        "av01" => "av1",
        "vp09" => "ivf",
        _ => "bin"
    };
}
