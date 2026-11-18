using FHT.Access.Domain.Entities;

namespace FHT.Access.Application.Services;

/// <summary>
/// Vigência do plano no totem. O dia de endsOn ainda libera. O dia seguinte bloqueia,
/// no fuso da unidade (Campo Grande, UTC−4, sem horário de verão).
/// validUntil chega da API como meia-noite UTC da data civil.
/// </summary>
public static class ContractAccess
{
    public const string Reason = "contract_expired";
    public const string PublicMessage = "Plano vencido. Procure a recepção.";
    public static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(2);
    const int UnitUtcOffsetHours = -4;

    public static bool Ended(DateTime? validUntil, DateTime? utcNow = null)
    {
        if (validUntil is null)
            return false;

        var until = validUntil.Value;
        if (until.Kind == DateTimeKind.Local)
            until = until.ToUniversalTime();
        else if (until.Kind == DateTimeKind.Unspecified)
            until = DateTime.SpecifyKind(until, DateTimeKind.Utc);

        var endDate = DateOnly.FromDateTime(until);
        var now = utcNow ?? DateTime.UtcNow;
        if (now.Kind == DateTimeKind.Local)
            now = now.ToUniversalTime();
        var local = now.AddHours(UnitUtcOffsetHours);
        var today = new DateOnly(local.Year, local.Month, local.Day);
        return today > endDate;
    }

    public static bool BlocksAutomaticEntry(Member member, DateTime? utcNow = null)
    {
        if (string.Equals(member.PersonType, "employee", StringComparison.OrdinalIgnoreCase))
            return false;

        var operational = member.OperationalStatus ?? string.Empty;
        if (operational is "expired_recently" or "inactive")
            return true;

        var reason = member.ReasonCode ?? string.Empty;
        if (reason is Reason or "expired_recently")
            return true;

        return Ended(member.ValidUntil, utcNow);
    }
}
