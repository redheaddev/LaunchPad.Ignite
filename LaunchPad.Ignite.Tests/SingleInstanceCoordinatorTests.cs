using LaunchPad.Ignite.App;

namespace LaunchPad.Ignite.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task AcquireOrReplaceExisting_ShutsDownPreviousInstanceBeforeTakingOwnership()
    {
        var id = Guid.NewGuid().ToString("N");
        var lockPath = Path.Combine(Path.GetTempPath(), $"LaunchPad.Ignite.{id}.lock");
        var pipeName = $"LaunchPad.Ignite.{id}";
        var shutdownRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = SingleInstanceCoordinator.AcquireOrReplaceExisting(lockPath, pipeName, TimeSpan.FromSeconds(5));

        Assert.NotNull(first);
        first.StartListening(() =>
        {
            shutdownRequested.TrySetResult();
            first.Dispose();
        });

        try
        {
            var secondTask = Task.Run(() => SingleInstanceCoordinator.AcquireOrReplaceExisting(lockPath, pipeName, TimeSpan.FromSeconds(5)));
            await shutdownRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(second);
        }
        finally
        {
            first.Dispose();
            File.Delete(lockPath);
        }
    }
}