namespace FHT.Access.Domain.Enums;

/// <summary>Mutually exclusive operating modes. Only Automatic enables continuous recognition.</summary>
public enum AccessOperatingMode
{
    Automatic = 0,
    Attendant = 1,
    Enrollment = 2,
    Maintenance = 3
}
