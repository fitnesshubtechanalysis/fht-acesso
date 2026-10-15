using FHT.Access.Application.Services;

namespace FHT.Access.Tests;

public class ReleasedMemberLatchTests
{
    private static readonly Guid Released = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Next = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime T0 = new(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Same_person_stays_on_the_idle_screen()
    {
        var latch = new ReleasedMemberLatch();
        latch.Arm(Released);

        Assert.Equal(LatchDecision.Hold, latch.Observe(Released, T0));
        Assert.Equal(LatchDecision.Hold, latch.Observe(Released, T0.AddSeconds(3)));
    }

    [Fact]
    public void Next_person_is_recognized_without_an_empty_camera()
    {
        var latch = new ReleasedMemberLatch();
        latch.Arm(Released);

        Assert.Equal(LatchDecision.Hold, latch.Observe(Next, T0));
        Assert.Equal(LatchDecision.RecognizeOther, latch.Observe(Next, T0.AddMilliseconds(120)));
    }

    [Fact]
    public void Motion_without_the_released_face_opens_recognition()
    {
        var latch = new ReleasedMemberLatch();
        latch.Arm(Released);

        Assert.Equal(LatchDecision.Hold, latch.Observe(null, T0));
        Assert.Equal(
            LatchDecision.OpenRecognition,
            latch.Observe(null, T0.Add(ReleasedMemberLatch.OpenWhenReleasedNotSeen)));
    }

    [Fact]
    public void Seeing_the_released_person_again_restarts_the_wait()
    {
        var latch = new ReleasedMemberLatch();
        latch.Arm(Released);

        latch.Observe(null, T0);
        Assert.Equal(LatchDecision.Hold, latch.Observe(Released, T0.AddMilliseconds(500)));
        Assert.Equal(
            LatchDecision.Hold,
            latch.Observe(null, T0.AddMilliseconds(500).Add(ReleasedMemberLatch.OpenWhenReleasedNotSeen).AddMilliseconds(-1)));
    }
}
