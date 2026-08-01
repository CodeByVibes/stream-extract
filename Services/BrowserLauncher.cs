namespace StreamExtract.Services;

public static class BrowserLauncher
{
    private static readonly HashSet<string> _allowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cudacoder.com",
        "github.com",
        "mkvtoolnix.download",
        "wiki.gpac.io",
    };

    public static bool TryOpen(string url, out string error)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !_allowedHosts.Contains(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (uri.Port != -1 && uri.Port != 443))
        {
            error = "The URL is not on the allowlist or is not a secure https URL.";
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
