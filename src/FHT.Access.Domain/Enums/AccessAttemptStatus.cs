namespace FHT.Access.Domain.Enums;

public enum AccessAttemptStatus
{
    Recognized = 0,
    Validating = 1,
    Denied = 2,
    Released = 3,
    PassageConfirmed = 4,
    TimedOut = 5,
    Failed = 6
}
