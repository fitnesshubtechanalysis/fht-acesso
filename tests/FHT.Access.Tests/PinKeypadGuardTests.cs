using FHT.Access.Application.Services;

namespace FHT.Access.Tests;

public class PinKeypadGuardTests
{
    [Fact]
    public void Fifth_unknown_code_locks_for_sixty_seconds()
    {
        var guard = new PinKeypadGuard();
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++)
            guard.RegisterFailure(now);
        Assert.False(guard.IsLocked(now));

        guard.RegisterFailure(now);
        Assert.True(guard.IsLocked(now));
        Assert.Equal(TimeSpan.FromSeconds(60), guard.Remaining(now));
        Assert.False(guard.IsLocked(now.AddSeconds(60)));
    }

    [Fact]
    public void Known_code_clears_the_failure_count()
    {
        var guard = new PinKeypadGuard();
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        guard.RegisterFailure(now);
        guard.RegisterFailure(now);
        guard.RegisterSuccess();
        for (var i = 0; i < 4; i++)
            guard.RegisterFailure(now);
        Assert.False(guard.IsLocked(now));
    }
}
