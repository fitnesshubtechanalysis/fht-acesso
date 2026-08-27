namespace FHT.Access.Domain.Enums;

public enum TurnstileConnectionState
{
    Disconnected = 0,
    Discovering = 1,
    Connecting = 2,
    Connected = 3,
    WaitingPassage = 4,
    Reconnecting = 5,
    Error = 6
}
