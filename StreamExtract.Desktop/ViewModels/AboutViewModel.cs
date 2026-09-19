using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using StreamExtract.Services;

namespace StreamExtract.Desktop.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public const string CudacoderUrl = "https://cudacoder.com";
    public const string MkvToolnixUrl = "https://mkvtoolnix.download/";
    public const string Mp4boxUrl = "https://wiki.gpac.io/MP4Box/MP4Box/";

    public string AppTitle { get; }
    public string Description => "A modern desktop tool for extracting streams, chapters, attachments, tags, cue sheets, and timestamps from MKV and MP4 media containers.";
    public string LicenseInfo => "MIT License • Powered by MKVToolNix and GPAC";

    public AboutViewModel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = version is null ? "1.0" : $"{version.Major}.{version.Minor}";
        AppTitle = $"StreamExtract v{versionString}";
    }

    [RelayCommand]
    private void OpenCudacoder() => BrowserLauncher.TryOpen(CudacoderUrl, out _);

    [RelayCommand]
    private void OpenMkvToolnix() => BrowserLauncher.TryOpen(MkvToolnixUrl, out _);

    [RelayCommand]
    private void OpenMp4box() => BrowserLauncher.TryOpen(Mp4boxUrl, out _);
}
