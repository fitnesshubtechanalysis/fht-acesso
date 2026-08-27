namespace FHT.Access.Domain.Enums;

/// <summary>Explicit UI / pipeline states (avoid scattered booleans).</summary>
public enum AccessUiState
{
    AutomaticIdle = 0,
    FaceDetected = 1,
    Recognizing = 2,
    Recognized = 3,
    Unknown = 4,
    Authorized = 5,
    Denied = 6,
    WaitingPassage = 7,
    PassageConfirmed = 8,

    AttendantLogin = 20,
    AttendantDashboard = 21,
    MemberSearch = 22,
    Enrollment = 23,
    EnrollmentCompleted = 24,
    ManualRelease = 25,

    Maintenance = 30,
    Error = 40
}
