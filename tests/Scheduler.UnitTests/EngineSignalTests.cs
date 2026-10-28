using Scheduler.Engine;

namespace Scheduler.UnitTests;

public class EngineSignalTests
{
    [Fact]
    public async Task WaitAsync_ReturnsTrue_WhenWoken()
    {
        var signal = new EngineSignal();
        signal.Wake();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task WaitAsync_ReturnsFalse_WhenNotWokenBeforeTimeout()
    {
        var signal = new EngineSignal();

        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task Wake_TwiceBeforeWaiting_OnlyWakesOnce()
    {
        var signal = new EngineSignal();
        signal.Wake();
        signal.Wake();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }
}
