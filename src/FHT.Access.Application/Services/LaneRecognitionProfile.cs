using FHT.Access.Domain.Abstractions;

namespace FHT.Access.Application.Services;

/// <summary>Per-lane recognition timing and face-detection tuning.</summary>
public sealed class LaneRecognitionProfile
{
    public TimeSpan ApproachHold { get; init; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan SettleBeforeIdentify { get; init; } = TimeSpan.FromSeconds(1.4);
    public int IdentifyAttempts { get; init; } = 16;
    public FaceDetectionOptions? FaceDetection { get; init; }
    public TimeSpan PassageFailureDisplay { get; init; } = TimeSpan.FromSeconds(1.5);
    public bool ImmediateRetryAfterPassageFailure { get; init; } = true;

    public static LaneRecognitionProfile Entry { get; } = new();

    public static LaneRecognitionProfile Exit { get; } = new()
    {
        ApproachHold = TimeSpan.FromMilliseconds(800),
        SettleBeforeIdentify = TimeSpan.FromMilliseconds(900),
        IdentifyAttempts = 18,
        FaceDetection = FaceDetectionOptions.ExitDistance,
        PassageFailureDisplay = TimeSpan.FromSeconds(1.5),
    };
}
