using System.Diagnostics;
using System.Reflection;

namespace StreamExtract;

public sealed partial class AboutDialog : Form
{
    private const string CudacoderUrl = "https://cudacoder.com";
    private const string MkvToolnixUrl = "https://mkvtoolnix.download/";
    private const string Mp4boxUrl = "https://wiki.gpac.io/MP4Box/MP4Box/";

    public AboutDialog()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = $"{version!.Major}.{version.Minor}";
        lblAbout.Text = $"StreamExtract v{versionString}";

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StreamExtract.Resources.app_logo.png");
            if (stream != null) pbLogo.Image = Image.FromStream(stream);
        }
        catch { }
    }

    private void PbLogo_Click(object? sender, EventArgs e) => OpenUrl(CudacoderUrl);

    private void LlblMkvToolnix_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) => OpenUrl(MkvToolnixUrl);

    private void LlblMp4box_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) => OpenUrl(Mp4boxUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
