using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;

namespace FHT.Access.Application.Services;

public sealed class DeviceService
{
    private readonly ISettingsStore _settings;

    public DeviceService(ISettingsStore settings)
    {
        _settings = settings;
    }

    public Task<Device?> GetDeviceAsync(CancellationToken cancellationToken = default)
        => _settings.GetDeviceAsync(cancellationToken);

    public Task SaveDeviceAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        return _settings.SaveDeviceAsync(device, cancellationToken);
    }

    public Task<TurnstileConfig?> GetTurnstileConfigAsync(CancellationToken cancellationToken = default)
        => _settings.GetTurnstileConfigAsync(cancellationToken);

    public Task SaveTurnstileConfigAsync(TurnstileConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return _settings.SaveTurnstileConfigAsync(config, cancellationToken);
    }

    public Task<SyncState> GetSyncStateAsync(CancellationToken cancellationToken = default)
        => _settings.GetSyncStateAsync(cancellationToken);

    public Task SaveSyncStateAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _settings.SaveSyncStateAsync(state, cancellationToken);
    }
}
