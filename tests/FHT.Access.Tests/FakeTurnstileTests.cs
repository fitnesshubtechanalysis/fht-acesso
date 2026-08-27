using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using FHT.Access.Toletus;

namespace FHT.Access.Tests;

public class FakeTurnstileTests
{
    [Fact]
    public async Task Connect_ReleaseEntry_RaisesPassageDetected_Within2Seconds()
    {
        await using var fake = new FakeTurnstile();
        var tcs = new TaskCompletionSource<PassageOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PassageReceived += (_, outcome) => tcs.TrySetResult(outcome);

        await fake.ConnectAsync(new TurnstileConfig { UseFake = true });
        Assert.Equal(TurnstileConnectionState.Connected, fake.State);

        await fake.ReleaseEntryAsync();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(PassageOutcome.PassageDetected, await tcs.Task);
        Assert.Equal(TurnstileConnectionState.Connected, fake.State);
    }
}
