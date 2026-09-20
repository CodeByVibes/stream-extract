using StreamExtract.Desktop.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Tests for <see cref="AvaloniaDialogService.AwaitCloseAsync"/>, the await-on-close contract used
/// when a message dialog has no owner window. Previously the ownerless branch called
/// <c>Show()</c> and returned immediately, so awaiting callers resumed before the user had
/// dismissed the dialog.
/// </summary>
/// <remarks>
/// A fake <see cref="IClosable"/> is used because constructing an Avalonia <c>Window</c> requires a
/// live <c>IWindowingPlatform</c>.
/// </remarks>
public class AvaloniaDialogServiceTests
{
    private sealed class FakeClosable : IClosable
    {
        public event EventHandler? Closed;

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public async Task AwaitCloseAsync_DoesNotCompleteWhileDialogIsOpen()
    {
        var closable = new FakeClosable();
        var closed = AvaloniaDialogService.AwaitCloseAsync(closable);

        Assert.False(closed.IsCompleted);

        // Give any spurious continuation a chance to run before asserting again.
        await Task.Delay(50);
        Assert.False(closed.IsCompleted);

        closable.Close();
        await closed;
    }

    [Fact]
    public async Task AwaitCloseAsync_CompletesWhenDialogCloses()
    {
        var closable = new FakeClosable();
        var closed = AvaloniaDialogService.AwaitCloseAsync(closable);

        closable.Close();

        await closed;
        Assert.True(closed.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AwaitCloseAsync_CompletesOnlyOnceWhenClosedRepeatedly()
    {
        var closable = new FakeClosable();
        var closed = AvaloniaDialogService.AwaitCloseAsync(closable);

        // TrySetResult must make a double close harmless instead of throwing.
        closable.Close();
        closable.Close();

        await closed;
        Assert.True(closed.IsCompletedSuccessfully);
    }
}
