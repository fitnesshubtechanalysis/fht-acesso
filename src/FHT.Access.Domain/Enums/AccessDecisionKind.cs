namespace FHT.Access.Domain.Enums;

public enum AccessDecisionKind
{
    AllowRegular,
    AllowTolerance,
    RequireReception,
    DenyAdministrative,
    DenySecurity,
    AllowManual,
    AllowFreeExit
}
