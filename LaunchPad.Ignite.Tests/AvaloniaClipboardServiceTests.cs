using LaunchPad.Ignite.App.Services.Clipboard;

namespace LaunchPad.Ignite.Tests;

public sealed class AvaloniaClipboardServiceTests
{
    [Fact]
    public async Task ClipboardExpiryClearsWhenClipboardStillHoldsTheCopiedValue()
    {
        var clipboard = new FakeClipboardAccess { ReadValue = "secret" };
        var service = new AvaloniaClipboardService(() => clipboard);

        await service.CopyWithExpiryAsync("secret", TimeSpan.FromMilliseconds(20));

        await clipboard.Cleared.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, clipboard.ClearCount);
    }

    [Fact]
    public async Task ClipboardExpiryLeavesClipboardAloneWhenItChanged()
    {
        var clipboard = new FakeClipboardAccess { ReadValue = "something else" };
        var service = new AvaloniaClipboardService(() => clipboard);

        await service.CopyWithExpiryAsync("secret", TimeSpan.FromMilliseconds(20));

        await Task.Delay(300);

        Assert.Equal(0, clipboard.ClearCount);
    }

    private sealed class FakeClipboardAccess : IClipboardAccess
    {
        public string? ReadValue { get; init; }

        public int ClearCount { get; private set; }

        public TaskCompletionSource Cleared { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SetTextAsync(string value) => Task.CompletedTask;

        public Task<string?> GetTextAsync() => Task.FromResult(ReadValue);

        public Task ClearAsync()
        {
            ClearCount++;
            Cleared.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
