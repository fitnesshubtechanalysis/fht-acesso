using FHT.Access.Application.Abstractions;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.Infrastructure.Settings;

public sealed class AppSettingsDeviceContext(AppSettings settings) : IAccessDeviceContext
{
    public string? UnitId => settings.UnitId;
    public string? DeviceId => settings.DeviceId;
    public string? DeviceSecret => settings.DeviceSecret;
}
