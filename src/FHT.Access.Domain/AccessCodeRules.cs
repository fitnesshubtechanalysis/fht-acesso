namespace FHT.Access.Domain;

/// <summary>Código digitado na catraca: 4 ou 6 dígitos, o mesmo valor gravado na gestão.</summary>
public static class AccessCodeRules
{
    public static bool IsAccepted(string? digits)
        => digits is { Length: 4 or 6 } && digits.All(char.IsDigit);

    public static List<string> Normalize(IEnumerable<string?>? codes)
    {
        return (codes ?? [])
            .Select(code => new string((code ?? string.Empty).Where(char.IsDigit).ToArray()))
            .Where(IsAccepted)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
