using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Hydrates presence from DB on startup; one-time migration from legacy AccessEvents.
/// </summary>
public sealed class PresenceBootstrapService
{
    private readonly IPresenceRepository _presence;
    private readonly IAccessEventRepository _events;
    private readonly IDiagnosticLog? _log;

    public PresenceBootstrapService(
        IPresenceRepository presence,
        IAccessEventRepository events,
        IDiagnosticLog? log = null)
    {
        _presence = presence;
        _events = events;
        _log = log;
    }

    public async Task InitializeAsync(string unitId, CancellationToken ct = default)
    {
        var existing = await _presence.GetAllAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            var cleared = 0;
            foreach (var p in existing)
            {
                if (p.State is not (PresenceStateKind.EntryPending or PresenceStateKind.ExitPending))
                    continue;

                p.State = p.State == PresenceStateKind.EntryPending
                    ? PresenceStateKind.Outside
                    : PresenceStateKind.Inside;
                p.PendingAttemptId = null;
                p.Version++;
                p.UpdatedAt = DateTime.UtcNow;
                await _presence.UpsertAsync(p, ct).ConfigureAwait(false);
                cleared++;
            }

            _log?.Information(
                $"[Presence] Loaded {existing.Count} presence row(s) from DB" +
                (cleared > 0 ? $"; cleared {cleared} stale pending." : "."));
            return;
        }

        _log?.Information("[Presence] Empty — inferring from AccessEvents (PassageConfirmed only).");
        // Members currently inside are derived lazily on first recognition via legacy IsMemberPresentAsync.
        await Task.CompletedTask;
    }
}
