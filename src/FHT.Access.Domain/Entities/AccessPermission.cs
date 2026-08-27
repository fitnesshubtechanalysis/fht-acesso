namespace FHT.Access.Domain.Entities;

/// <summary>
/// Local permission snapshot. ReasonCode is for internal use only — never surface financial details to the student UI.
/// </summary>
public sealed class AccessPermission
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public bool Allowed { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? ReasonCode { get; set; }
}
