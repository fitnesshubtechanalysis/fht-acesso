namespace FHT.Access.Application.Services;

/// <summary>
/// Teclado da catraca. Seis dígitos consultam na hora. Quatro aguardam mais números,
/// para não cortar um código de seis no meio da digitação.
/// </summary>
public static class KeypadBuffer
{
    public readonly record struct Step(string Buffer, string? SubmitNow, bool WaitForMoreDigits);

    public static Step Apply(string buffer, string? incoming, bool bufferExpired)
    {
        if (bufferExpired)
            buffer = string.Empty;

        var digits = Digits(incoming);
        if (digits.Length is 4 or 6)
            return new Step(string.Empty, digits, false);
        if (digits.Length > 6)
            return new Step(string.Empty, digits[^6..], false);
        if (digits.Length == 0)
            return new Step(buffer, null, false);

        buffer += digits;
        if (buffer.Length > 6)
            buffer = buffer[^6..];
        if (buffer.Length == 6)
            return new Step(string.Empty, buffer, false);
        if (buffer.Length == 4)
            return new Step(buffer, null, true);
        return new Step(buffer, null, false);
    }

    public static Step Confirm(string buffer) => new(string.Empty, buffer, false);

    public static string Digits(string? raw)
        => new((raw ?? string.Empty).Where(char.IsDigit).ToArray());
}
