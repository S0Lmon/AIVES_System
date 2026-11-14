using AIVES.BLL.Services.Grading;

namespace AIVES.Tests;

public sealed class GradingWorkSignalTests
{
    [Fact]
    public async Task SignalWakesTheWorkerWithoutWaitingForItsPollInterval()
    {
        var signal = new GradingWorkSignal();
        signal.Signal();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task WorkerWaitTimesOutWhenNoWorkWasSignalled()
    {
        var signal = new GradingWorkSignal();

        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }
}
