namespace StreamExtract.Desktop.Services;

public interface IDialogService
{
    Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(string title, bool allowMultiple);
    Task<string?> ShowOpenFolderDialogAsync(string title);
    Task ShowMessageAsync(string title, string message);
    Task ShowAboutDialogAsync();
}
