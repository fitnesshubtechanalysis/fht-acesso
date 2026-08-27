namespace FHT.Access.Infrastructure.Http;

public static class GestaoUrl
{
    /// <summary>
    /// Normalizes the Gestão API origin so HttpClient can combine relative paths like
    /// <c>api/v1/access/device-auth</c>. Accepts host-only values and strips a trailing /api/v1.
    /// </summary>
    public static Uri ResolveBaseAddress(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (value.Length == 0)
        {
            throw new InvalidOperationException(
                "Informe a Base URL da Gestão (ex.: http://localhost:4010).");
        }

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;

        if (value.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            value = value[..^"/api/v1".Length].TrimEnd('/');

        if (!Uri.TryCreate(value + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Base URL inválida. Use http://localhost:4010 ou a URL pública da API (https://…).");
        }

        return uri;
    }
}
