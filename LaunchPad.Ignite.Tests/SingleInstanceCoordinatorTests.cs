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
        Task<SingleInstanceCoordinator?>? secondTask = null;
        SingleInstanceCoordinator? second = null;

        Assert.NotNull(first);
        first.StartListening(() =>
        {
            shutdownRequested.TrySetResult();
            first.Dispose();
        });

        try
        {
            secondTask = Task.Run(() => SingleInstanceCoordinator.AcquireOrReplaceExisting(lockPath, pipeName, TimeSpan.FromSeconds(5)));
            var shutdownOrSecondAcquired = await Task.WhenAny(
                shutdownRequested.Task,
                secondTask,
                first.ListenerTask!);
            if (shutdownOrSecondAcquired == secondTask)
            {
                using var unexpectedSecond = await secondTask;
                Assert.True(unexpectedSecond is null, "The second coordinator acquired ownership before requesting shutdown.");
            }

            if (shutdownOrSecondAcquired == first.ListenerTask && !shutdownRequested.Task.IsCompleted)
            {
                await first.ListenerTask;
                Assert.Fail("The listener stopped before receiving a shutdown request.");
            }

            await shutdownRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(second);
        }
        finally
        {
            if (second is null && secondTask is { IsCompletedSuccessfully: true })
            {
                second = secondTask.Result;
            }

            second?.Dispose();
            first.Dispose();
            File.Delete(lockPath);
        }
    }
}