namespace FHT.Access.Domain.Enums;

public enum RecognitionState
{
    Idle = 0,
    FaceDetected = 1,
    Identifying = 2,
    Matched = 3,
    Denied = 4,
    Cooldown = 5
}
