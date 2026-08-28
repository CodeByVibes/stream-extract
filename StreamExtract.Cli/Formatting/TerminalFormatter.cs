using System.Text;
using StreamExtract.Models;

namespace StreamExtract.Cli.Formatting;

public static class TerminalFormatter
{
    public static string FormatMediaInfo(MediaFileInfo info)
    {
        var b = new StringBuilder().AppendLine($"File: {info.FileName}");
        b.AppendLine("Tracks:");
        foreach (var t in info.Tracks) b.AppendLine($"  {t.Id}  {t.Type}  {t.Codec}  {t.TrackName} [{t.Language}]");
        b.AppendLine("Chapters:");
        foreach (var c in info.Chapters) b.AppendLine($"  {c.Id}  {c.Name} [{c.Language}]");
        b.AppendLine("Attachments:");
        foreach (var a in info.Attachments) b.AppendLine($"  {a.Id}  {a.FileName} ({a.MimeType}, {a.Size} bytes)");
        b.AppendLine("Tags:");
        foreach (var t in info.Tags) b.AppendLine($"  {t.Name} = {t.Value}");
        return b.ToString().TrimEnd();
    }
}
