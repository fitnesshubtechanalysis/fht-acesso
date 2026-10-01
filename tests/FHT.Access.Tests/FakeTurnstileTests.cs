using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using FHT.Access.Toletus;

namespace FHT.Access.Tests;

public class FakeTurnstileTests
{
    [Fact]
    public async Task ReleaseEntry_DoesNotInventPassage_UntilArmTurns()
    {
        await using var fake = new FakeTurnstile();
        var tcs = new TaskCompletionSource<PassageOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PassageReceived += (_, outcome) => tcs.TrySetResult(outcome);

        await fake.ConnectAsync(new TurnstileConfig { UseFake = true });
        Assert.Equal(TurnstileConnectionState.Connected, fake.State);

        await fake.ReleaseEntryAsync();
        Assert.Equal(TurnstileConnectionState.WaitingPassage, fake.State);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMilliseconds(900)));
        Assert.NotSame(tcs.Task, completed);

        fake.NotifyArmTurn();
        Assert.Equal(PassageOutcome.PassageDetected, await tcs.Task);
        Assert.Equal(TurnstileConnectionState.Connected, fake.State);
    }
}
