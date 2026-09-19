using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StreamExtract.Desktop.Services;
using StreamExtract.Desktop.ViewModels;
using StreamExtract.Desktop.Views;
using StreamExtract.Services;

namespace StreamExtract.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? mainWindow = null;
            var dialogService = new AvaloniaDialogService(() => mainWindow);

            INativeToolResolver resolver;
            try
            {
                resolver = new NativeToolResolver(AppContext.BaseDirectory);
            }
            catch (NativeToolValidationException ex)
            {
                // In unbundled / debug environment, we create a stub resolver or let the user know
                resolver = new FallbackNativeToolResolver(AppContext.BaseDirectory);
                System.Diagnostics.Debug.WriteLine($"[ToolValidator] Startup warning: {ex.Message}");
            }

            var viewModel = new MainWindowViewModel(resolver, dialogService);

            mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.MainWindow = mainWindow;

            // Trigger background update check
            _ = Task.Run(viewModel.CheckUpdateAsync);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private sealed class FallbackNativeToolResolver(string baseDirectory) : INativeToolResolver
    {
        public string Resolve(NativeToolId id) =>
            Path.Combine(baseDirectory, "tools", NativeTool.GetFilename(id));
    }
}
