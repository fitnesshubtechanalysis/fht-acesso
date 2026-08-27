namespace FHT.Access.Application.Abstractions;

public interface IAccessDeviceContext
{
    string? UnitId { get; }
    string? DeviceId { get; }
    string? DeviceSecret { get; }
}
