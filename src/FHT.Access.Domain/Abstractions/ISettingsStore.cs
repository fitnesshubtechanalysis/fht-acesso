using FHT.Access.Domain.Entities;

namespace FHT.Access.Domain.Abstractions;

public interface ISettingsStore
{
    Task<Device?> GetDeviceAsync(CancellationToken cancellationToken = default);
    Task SaveDeviceAsync(Device device, CancellationToken cancellationToken = default);

    Task<TurnstileConfig?> GetTurnstileConfigAsync(CancellationToken cancellationToken = default);
    Task SaveTurnstileConfigAsync(TurnstileConfig config, CancellationToken cancellationToken = default);

    Task<SyncState> GetSyncStateAsync(CancellationToken cancellationToken = default);
    Task SaveSyncStateAsync(SyncState state, CancellationToken cancellationToken = default);
}
