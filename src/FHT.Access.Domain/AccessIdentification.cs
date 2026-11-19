namespace FHT.Access.Domain;

/// <summary>
/// Como a pessoa se identificou. O valor digitado no teclado não entra no evento.
/// </summary>
public static class AccessIdentification
{
    public const string Facial = "facial";
    public const string Pin = "pin";
    public const string Manual = "manual";
    public const string Card = "card";

    public static string? MethodForSource(string? source)
    {
        return (source ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "face" or "facial" or "biometric" => Facial,
            "pin" or "keypad" => Pin,
            "manual" => Manual,
            "rfid" or "card" or "qr" => Card,
            _ => null
        };
    }
}
